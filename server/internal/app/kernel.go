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

// Kernel owns the platform services a module may reach for directly, plus the registry through
// which modules reach each other.
type Kernel struct {
	Log   *slog.Logger
	Cfg   *config.Config
	SQL   *database.DB
	Mongo *mongodb.DB
	Bus   *eventbus.Bus

	// Spread into every Connect handler this server builds; modules do not assemble their own. A
	// handler that skipped these would return raw internal errors to callers, which is a security
	// problem rather than an inconsistency.
	RPC []connect.HandlerOption

	mu       sync.RWMutex
	services map[reflect.Type]any

	modules []Module
	started []Stopper

	// Empty until AllowUnprotectedRoutes is called. A module absent from it that declares Public or
	// SignedIn fails the boot.
	unprotected map[string]struct{}

	// Built once, by Routes.
	routes []Route
}

// Short on purpose: this bounds the cleanup after a failed Start, where nothing has served a
// request yet, so there is no work to drain — only handles to release.
const shutdownGrace = 10 * time.Second

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

// Cross-module service lookups do not depend on mount order. Only migrations and shutdown do.
//
// Two modules answering the same ID panics: the ID namespaces a SQL schema, a Mongo collection
// prefix, a configuration section and an access permission, so a collision silently merges four
// things meant to be separate — and the second module inherits whatever treatment the first got.
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

// Call it before Routes; a module absent from the list that declares an unprotected route fails the
// boot.
//
// The list lives at the composition root rather than in each module, because exemption is a
// security decision. A module that could exempt itself would opt out by editing one line of its own
// file — and the documented way to add a module is to copy an existing one, which is exactly how a
// stray Public() travels.
//
// It buys a module permission to ask, not blanket exemption: every route it serves still states its
// own policy, and its administrative surface still names its own permission.
func (k *Kernel) AllowUnprotectedRoutes(moduleIDs ...string) {
	k.unprotected = make(map[string]struct{}, len(moduleIDs))
	for _, id := range moduleIDs {
		k.unprotected[id] = struct{}{}
	}
}

// AccessModules are the modules that actually gate a route on their own <id>.access, in mount
// order.
//
// The authorization module mints its catalogue from this rather than from every mounted module.
// Minting one per mounted module produced grantable, official-looking permissions for the modules
// nothing checks them on, which an administrator could spend a morning granting to no effect.
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

// Every module finishes a stage before any module starts the next, which is what makes registration
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
				// Detached from the boot context: handing the cleanup the very context whose
				// cancellation may have caused the failure makes every Stop fail immediately.
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
	// declared — is complete only once every Start has returned.
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

// Shutdown keeps going after a failure and reports every error it saw: one module refusing to stop
// must not strand the rest, and an earlier module's failure must not be swallowed by a later one's.
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

// Routes are deliberately not sorted. Sorting would imply that an earlier route can shadow a later
// one, which is not how net/http resolves patterns: the most specific match wins regardless of
// registration order, and two modules claiming the identical pattern is a design mistake the mux is
// right to panic on.
func (k *Kernel) Routes() []Route {
	// Collected once. Two callers ask — the mux that mounts them, and AccessModules — and a
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
			// Stamped over anything the module put there: a module that could name itself could
			// name another, and this name is what decides whose permission applies.
			route.Module = m.ID()

			// Both refusals happen at boot. A route with no policy is a route somebody forgot; a
			// route that checks nothing, from a module the composition root did not exempt, is a
			// route somebody opened without saying so at the root. Neither is something to discover
			// from a request that should have been refused.
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

// Call Provide from Register.
//
// T is meant to be an interface declared in the providing module's api package. Publishing a
// concrete struct works but defeats the point: consumers would then compile against your internals.
//
// Providing the same type twice panics. Two modules claiming the same contract is a design mistake
// worth failing loudly on, at startup, rather than silently letting the last one win.
func Provide[T any](k *Kernel, impl T) {
	t := reflect.TypeFor[T]()

	k.mu.Lock()
	defer k.mu.Unlock()

	if _, dup := k.services[t]; dup {
		panic(fmt.Sprintf("app: service %s provided twice", t))
	}
	k.services[t] = impl
}

// Call Use from Start or from inside a handler, never from Register: at Register time the module
// that provides T may not have run yet.
//
// It panics when T is missing — better a stack trace at boot than a nil dereference on the first
// request that happens to reach that path. Use TryUse when the dependency is genuinely optional.
func Use[T any](k *Kernel) T {
	v, ok := TryUse[T](k)
	if !ok {
		panic(fmt.Sprintf("app: no service provides %s", reflect.TypeFor[T]()))
	}
	return v
}

// UseAll collects every mounted MODULE that satisfies T, in mount order. A different question from
// Use: it reads the mount list rather than the service registry, so a module answers for itself
// rather than by registering something, and any number of them — none included — may answer.
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

// TryUse is for optional dependencies: a feature that lights up only when some module is mounted.
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

// A thin wrapper over the bus, kept here so a module never has to reach past the kernel.
//
// fn runs synchronously on the publisher's goroutine, inside the publisher's context. Keep it
// quick.
func Subscribe[E any](k *Kernel, fn func(context.Context, E)) {
	eventbus.Subscribe(k.Bus, fn)
}

func Publish[E any](k *Kernel, ctx context.Context, e E) {
	eventbus.Publish(k.Bus, ctx, e)
}
