// Package app is the kernel: the Module contract, the service registry, and the boot sequence.
//
// Modules receive the kernel; the kernel never imports a module. That one-way rule is what keeps
// the dependency graph acyclic, and tools/archlint fails the build if it is broken.
package app

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"reflect"
	"slices"
	"sync"
	"time"

	"connectrpc.com/connect"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/config"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/database"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/mongodb"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/rpc"
)

// Kernel owns the platform services every module is allowed to reach for directly, plus the
// registry through which modules reach each other.
type Kernel struct {
	Log   *slog.Logger
	Cfg   *config.Config
	SQL   *database.DB
	Mongo *mongodb.DB
	Bus   *eventbus.Bus

	// RPC are the options every Connect handler in this server is built with: error mapping,
	// and whatever else the whole server should agree on. Spread them when building a handler:
	//
	//	path, handler := healthv1connect.NewHealthServiceHandler(svc, k.RPC...)
	//
	// Modules do not assemble their own. A handler that skipped these would return raw internal
	// errors to callers, which is a security problem rather than an inconsistency.
	RPC []connect.HandlerOption

	mu       sync.RWMutex
	services map[reflect.Type]any

	modules []Module
	started []Stopper

	// unprotected are the modules the composition root permits to serve a route that checks no
	// permission. Empty until AllowUnprotectedRoutes is called, and a module absent from it that
	// declares Public or SignedIn fails the boot.
	unprotected map[string]struct{}

	// routes is the collected route table, built once by Routes.
	routes []Route
}

// shutdownGrace bounds the cleanup that follows a failed Start. Short on purpose: nothing has
// served a request yet, so there is no work to drain — only handles to release.
const shutdownGrace = 10 * time.Second

// NewKernel wires the platform services together. Modules are added afterwards with Mount.
func NewKernel(
	log *slog.Logger,
	cfg *config.Config,
	sqlDB *database.DB,
	mongoDB *mongodb.DB,
	bus *eventbus.Bus,
) *Kernel {
	return &Kernel{
		Log:      log,
		Cfg:      cfg,
		SQL:      sqlDB,
		Mongo:    mongoDB,
		Bus:      bus,
		RPC:      rpc.HandlerOptions(log),
		services: make(map[reflect.Type]any),
	}
}

// Mount adds modules in the order they should register. Cross-module service lookups do not depend
// on this order. Only migrations and shutdown do.
//
// Two modules answering the same ID is a programming error and panics, for the reason Provide
// panics on a duplicate type: the ID namespaces a SQL schema, a Mongo collection prefix, a
// configuration section and an access permission, so a collision silently merges four things that
// were meant to be separate — and the second module inherits whatever treatment the first got.
func (k *Kernel) Mount(mods ...Module) {
	for _, m := range mods {
		for _, existing := range k.modules {
			if existing.ID() == m.ID() {
				panic(fmt.Sprintf("app: two modules claim the id %q", m.ID()))
			}
		}
		k.modules = append(k.modules, m)
	}
}

// AllowUnprotectedRoutes names the modules permitted to serve a route whose policy checks no
// permission. Call it before Routes; a module absent from the list that declares one fails the boot.
//
// It lives at the composition root rather than as something a module declares about itself, because
// exemption is a security decision. A module that could exempt itself would opt out by editing one
// line of its own file — and the documented way to add a module is to copy an existing one, which is
// exactly how a stray Public() travels. Named at the root, it is a one-line diff in the file a
// reviewer already opens to learn what this server is made of.
//
// It buys a module permission to ask, not blanket exemption: every route it serves still states its
// own policy, and its administrative surface still names its own permission.
func (k *Kernel) AllowUnprotectedRoutes(moduleIDs ...string) {
	k.unprotected = make(map[string]struct{}, len(moduleIDs))
	for _, id := range moduleIDs {
		k.unprotected[id] = struct{}{}
	}
}

// AccessModules are the modules that actually gate a route on their own <id>.access, in mount order.
//
// The authorization module mints its catalogue from this rather than from every mounted module. The
// difference is not cosmetic: minting one per mounted module produced grantable, official-looking
// permissions for the four modules nothing checks them on, which an administrator could spend a
// morning granting to no effect.
func (k *Kernel) AccessModules() []string {
	var out []string
	for _, route := range k.Routes() {
		if route.Policy.Kind() != PolicyModuleAccess {
			continue
		}
		if !slices.Contains(out, route.Module) {
			out = append(out, route.Module)
		}
	}
	return out
}

// Modules returns the mounted modules in registration order.
func (k *Kernel) Modules() []Module {
	return k.modules
}

// Boot runs the module lifecycle: Register for everyone, then Migrate for everyone, then Indexes
// for everyone, then Start for everyone. Splitting it this way is what makes the registration
// order irrelevant to service resolution.
//
// If any stage fails, the modules already started are stopped before the error is returned, so a
// failed boot does not leak goroutines or connections.
func (k *Kernel) Boot(ctx context.Context) error {
	for _, m := range k.modules {
		if err := m.Register(k); err != nil {
			return fmt.Errorf("register module %q: %w", m.ID(), err)
		}
		k.Log.DebugContext(ctx, "module registered", "module", m.ID())
	}

	for _, m := range k.modules {
		mig, ok := m.(Migrator)
		if !ok {
			continue
		}
		if err := k.SQL.Migrate(ctx, m.ID(), toDBMigrations(mig.Migrations())); err != nil {
			return fmt.Errorf("migrate module %q: %w", m.ID(), err)
		}
	}

	for _, m := range k.modules {
		ix, ok := m.(Indexer)
		if !ok {
			continue
		}
		if err := k.Mongo.EnsureIndexes(ctx, m.ID(), toDBIndexes(ix.Indexes())); err != nil {
			return fmt.Errorf("index module %q: %w", m.ID(), err)
		}
	}

	for _, m := range k.modules {
		if s, ok := m.(Starter); ok {
			if err := s.Start(ctx, k); err != nil {
				// A fresh deadline, detached from the boot context. Handing the cleanup the very
				// context whose cancellation may have caused the failure makes every Stop fail
				// immediately, which is the opposite of shutting down.
				stopCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), shutdownGrace)
				stopErr := k.Shutdown(stopCtx)
				cancel()
				return errors.Join(fmt.Errorf("start module %q: %w", m.ID(), err), stopErr)
			}
		}
		if s, ok := m.(Stopper); ok {
			k.started = append(k.started, s)
		}
		k.Log.DebugContext(ctx, "module started", "module", m.ID())
	}

	// Last, and only now: everything a Finalizer asks about — the route table, what every module
	// declared — is only complete once every Start has returned.
	for _, m := range k.modules {
		f, ok := m.(Finalizer)
		if !ok {
			continue
		}
		if err := f.Finalize(ctx, k); err != nil {
			stopCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), shutdownGrace)
			stopErr := k.Shutdown(stopCtx)
			cancel()
			return errors.Join(fmt.Errorf("finalize module %q: %w", m.ID(), err), stopErr)
		}
	}

	return nil
}

// Shutdown stops every started module in reverse order. It keeps going after a failure and reports
// every error it saw: one module refusing to stop must not strand the rest, and an earlier module's
// failure must not be swallowed by a later one's.
//
// It also drops the service registry. Leaving it populated meant a resolved service outlived the
// module that owned it — a handle to a store whose connections had just been closed.
func (k *Kernel) Shutdown(ctx context.Context) error {
	var errs []error
	for i := len(k.started) - 1; i >= 0; i-- {
		if err := k.started[i].Stop(ctx); err != nil {
			errs = append(errs, err)
		}
	}
	k.started = nil

	k.mu.Lock()
	k.services = make(map[reflect.Type]any)
	k.mu.Unlock()

	return errors.Join(errs...)
}

// Routes collects the endpoints contributed by every module, in registration order.
//
// Unlike the desktop original's Views, these are not sorted. Ordering would imply that an earlier
// route can shadow a later one, which is not how net/http resolves patterns: the most specific
// match wins regardless of registration order, and two modules claiming the identical pattern is a
// design mistake the mux is right to panic on.
func (k *Kernel) Routes() []Route {
	// Collected once. Two callers now ask — the mux that mounts them, and AccessModules — and a
	// module's Routes builds handlers, so asking twice would hand the mux one set and the access
	// question a different, unmounted set.
	if k.routes != nil {
		return k.routes
	}

	var routes []Route
	for _, m := range k.modules {
		c, ok := m.(RouteContributor)
		if !ok {
			continue
		}
		for _, route := range c.Routes(k) {
			// Stamped here, over anything the module put there, because the loop already knows
			// whose route this is and the module must not get a say. A module that could name
			// itself could name another, and the name is what decides whose permission applies.
			route.Module = m.ID()

			// Two refusals, both at boot, both loud. A route with no policy is a route somebody
			// forgot; a route that checks nothing, from a module the composition root did not
			// exempt, is a route somebody opened without saying so at the root. Neither is
			// something to discover from a request that should have been refused.
			if route.Policy.Kind() == PolicyUnset {
				panic(fmt.Sprintf(
					"app: module %q serves %q with no access policy; state one beside the pattern",
					m.ID(), route.Pattern))
			}
			if route.Policy.Unprotected() {
				if _, allowed := k.unprotected[m.ID()]; !allowed {
					panic(fmt.Sprintf(
						"app: module %q serves %q as %s, which checks no permission, and %q is "+
							"not named in the composition root's unprotected-route list",
						m.ID(), route.Pattern, route.Policy, m.ID()))
				}
			}

			routes = append(routes, route)
		}
	}

	k.routes = routes
	return routes
}

func toDBMigrations(in []Migration) []database.Migration {
	out := make([]database.Migration, len(in))
	for i, m := range in {
		out[i] = database.Migration{Name: m.Name, SQL: m.SQL}
	}
	return out
}

func toDBIndexes(in []Index) []mongodb.Index {
	out := make([]mongodb.Index, len(in))
	for i, ix := range in {
		keys := make([]mongodb.Key, len(ix.Keys))
		for j, k := range ix.Keys {
			keys[j] = mongodb.Key{Field: k.Field, Descending: k.Descending}
		}
		out[i] = mongodb.Index{
			Collection:  ix.Collection,
			Name:        ix.Name,
			Unique:      ix.Unique,
			Keys:        keys,
			ExpireAfter: ix.ExpireAfter,
		}
	}
	return out
}

// Provide publishes a module's service under the interface type T. Call it from Register.
//
// T is meant to be an interface declared in the providing module's api package. Publishing a
// concrete struct works but defeats the point: consumers would then compile against your
// internals.
//
//	app.Provide[notesapi.Service](k, svc)
//
// Providing the same type twice is a programming error and panics. Two modules claiming the same
// contract is a design mistake worth failing loudly on, at startup, rather than silently letting
// the last one win.
func Provide[T any](k *Kernel, impl T) {
	t := reflect.TypeFor[T]()

	k.mu.Lock()
	defer k.mu.Unlock()

	if _, dup := k.services[t]; dup {
		panic(fmt.Sprintf("app: service %s provided twice", t))
	}
	k.services[t] = impl
}

// Use resolves the service published under T. Call it from Start or from inside a handler, never
// from Register: at Register time the module that provides T may not have run yet.
//
// It panics when T is missing, because a module asking for a contract nobody implements cannot do
// anything useful: better a stack trace at boot than a nil dereference on the first request that
// happens to reach that path. Use TryUse when the dependency is genuinely optional.
func Use[T any](k *Kernel) T {
	v, ok := TryUse[T](k)
	if !ok {
		panic(fmt.Sprintf("app: no service provides %s", reflect.TypeFor[T]()))
	}
	return v
}

// UseAll collects every mounted MODULE that satisfies T, in mount order.
//
// A different question from Use, and worth keeping distinct: Use asks "who provides this contract"
// and expects one answer, while this asks "which modules are also a T" and expects any number —
// none included. It reads the mount list rather than the service registry, so a module answers for
// itself rather than by registering something.
//
// The case it exists for is a module describing itself to another: the authorization module asks
// which modules declare permissions, and no module has to know it is being asked.
func UseAll[T any](k *Kernel) []T {
	var out []T
	for _, m := range k.modules {
		if v, ok := m.(T); ok {
			out = append(out, v)
		}
	}
	return out
}

// TryUse resolves T, reporting whether it was found. Use it for optional dependencies, e.g. a
// feature that lights up only when some module is compiled in.
func TryUse[T any](k *Kernel) (T, bool) {
	t := reflect.TypeFor[T]()

	k.mu.RLock()
	defer k.mu.RUnlock()

	v, ok := k.services[t]
	if !ok {
		var zero T
		return zero, false
	}
	return v.(T), true
}

// Subscribe registers fn as a listener for events of type E published by any module. It is a thin,
// kernel-flavoured wrapper over the bus, kept here so a module never has to reach past the kernel.
//
// fn runs synchronously on the publisher's goroutine, inside the publisher's context. Keep it
// quick.
func Subscribe[E any](k *Kernel, fn func(context.Context, E)) {
	eventbus.Subscribe(k.Bus, fn)
}

// Publish delivers e to every subscriber of type E.
func Publish[E any](k *Kernel, ctx context.Context, e E) {
	eventbus.Publish(k.Bus, ctx, e)
}
