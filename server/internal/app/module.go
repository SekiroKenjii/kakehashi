package app

import (
	"context"
	"net/http"
	"time"
)

// Module is one bounded context inside the monolith. Everything a feature owns (its domain types,
// its tables, its services, its handlers) lives under internal/modules/<id>/ and is reachable from
// the outside only through that module's api package.
//
// A module goes through the lifecycle below, in order, with every module finishing a stage before
// any module starts the next one. That is what lets a module resolve another module's service in
// Start without caring which order the two were registered in.
//
//  1. Register: publish services, subscribe to events. Do not resolve anything here, because the
//     modules registered after you have not run yet.
//  2. Migrate: create or upgrade the SQL Server tables you own. Optional.
//  3. Indexes: declare the Mongo indexes your collections need. Optional.
//  4. Start: resolve dependencies, open connections, spawn goroutines. Optional.
//  5. Routes: hand the mux the endpoints you serve. Optional.
//  6. Stop: release what Start acquired, in reverse registration order. Optional.
type Module interface {
	// ID is a stable, lowercase identifier. It namespaces the module's tables, its Mongo
	// collections and its configuration keys, so changing it later is a migration, not a rename.
	ID() string

	// Register publishes this module's services into the kernel and wires up event subscriptions.
	// It must not resolve services from other modules.
	Register(k *Kernel) error
}

// Migrator is implemented by modules that own SQL Server tables.
type Migrator interface {
	// Migrations returns the module's ordered, append-only migration list. Once a migration has
	// shipped, never edit it. Add another one instead.
	Migrations() []Migration
}

// Migration is a single forward schema change.
//
// It mirrors platform/database.Migration rather than reusing it so that a module's module.go can
// declare its schema without importing the database package, which tools/archlint reserves for
// store/ packages. The kernel does the conversion.
type Migration struct {
	// Name has to be unique within the module and must never change once released; it is the
	// primary key the server uses to decide what has already been applied.
	Name string

	// SQL is one or more statements, run as a single T-SQL batch.
	SQL string
}

// Indexer is implemented by modules that store documents in Mongo.
type Indexer interface {
	// Indexes returns every index this module's collections need. Creating an index that already
	// exists is a no-op, so this list is declarative: state what should be true, not what changed.
	Indexes() []Index
}

// Index is one index on one of a module's collections. It mirrors platform/mongodb.Index for the
// same reason Migration mirrors its database counterpart.
type Index struct {
	// Collection must start with the module's ID and an underscore. The kernel rejects anything
	// else, which is the one namespacing rule here that a machine can check.
	Collection string

	// Name identifies the index. Always set it.
	Name string

	// Unique makes the index reject duplicate keys.
	Unique bool

	// Keys are the indexed fields, in order.
	Keys []IndexKey

	// ExpireAfter turns this into a TTL index: Mongo deletes a document once the indexed date is
	// that far in the past. Zero means an ordinary index. Mongo honours it only on an index over a
	// single date field, so a retention index is always its own index rather than a flag on the
	// compound one a read uses.
	ExpireAfter time.Duration
}

// IndexKey is one field in an Index.
type IndexKey struct {
	Field      string
	Descending bool
}

// Starter is implemented by modules that need to do work once every module has registered. This is
// the earliest point at which Use[T] is safe.
type Starter interface {
	Start(ctx context.Context, k *Kernel) error
}

// Finalizer is implemented by modules whose work depends on what every other module contributed —
// routes, declarations — rather than on the services it published.
//
// Finalize runs after every Start has returned, so the route table and every module's
// declarations are complete; Start runs module by module and cannot offer that. Finalize is the
// last stage before the server serves, so it is also the place to refuse a composition that does
// not add up.
type Finalizer interface {
	Finalize(ctx context.Context, k *Kernel) error
}

// Stopper is implemented by modules holding resources that outlive Start. Stop runs in reverse
// registration order and is given a context with a deadline, so respect cancellation rather than
// blocking shutdown.
type Stopper interface {
	Stop(ctx context.Context) error
}

// RouteContributor is implemented by modules that serve requests. The mux collects every route and
// mounts it; the kernel knows only that modules contribute something mountable, not what any
// particular one contributes.
type RouteContributor interface {
	// Routes is called once, after every module has started, so resolving another module's
	// service with Use here is safe. It receives the kernel because handlers need k.RPC; do not
	// stash the kernel in Start for that.
	Routes(k *Kernel) []Route
}

// Route is one endpoint a module serves.
type Route struct {
	// Policy is what a caller must be before Handler runs, and it is MANDATORY: the zero value is
	// not a policy but the absence of one, and Kernel.Routes refuses to collect a route that still
	// carries it. Forgetting is a failed boot rather than an open endpoint.
	Policy RoutePolicy

	// Pattern is a net/http ServeMux pattern.
	//
	// Connect's generated constructors return exactly this pair — a path prefix ending in a slash,
	// and a handler — so an RPC service is one Route with no adapting:
	//
	//	path, handler := healthv1connect.NewHealthServiceHandler(svc)
	//	return []app.Route{{Pattern: path, Handler: handler}}
	//
	// Plain HTTP endpoints (an OpenID Connect authorize endpoint, a health probe) use the same
	// struct with a pattern of their own.
	Pattern string

	// Handler serves the pattern.
	Handler http.Handler

	// Module is the ID of the module that contributed this route. A module never sets it:
	// Kernel.Routes stamps it over whatever was there, because this field decides which access
	// policy a request is checked against, and a module must not choose that for itself.
	// internal/app/server reads it to decide which routes to gate.
	Module string
}
