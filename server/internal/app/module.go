package app

import (
	"context"
	"net/http"
	"time"
)

// Module is one bounded context inside the monolith. Everything a feature owns lives under
// internal/modules/<id>/ and is reachable from the outside only through that module's api package.
//
// Every module finishes a lifecycle stage before any module starts the next, which is what lets a
// module resolve another module's service in Start without caring which order the two were
// registered in.
//
//  1. Register: publish services, subscribe to events. Do not resolve anything here, because the
//     modules registered after you have not run yet.
//  2. Migrate: the SQL Server tables you own. Optional.
//  3. Indexes: the Mongo indexes your collections need. Optional.
//  4. Start: resolve dependencies, open connections, spawn goroutines. Optional.
//  5. Routes: hand the mux the endpoints you serve. Optional.
//  6. Stop: release what Start acquired, in reverse registration order. Optional.
type Module interface {
	// Stable and lowercase. It namespaces the module's tables, its Mongo collections and its
	// configuration keys, so changing it later is a migration, not a rename.
	ID() string

	// Register must not resolve services from other modules.
	Register(k *Kernel) error
}

// Implemented by modules that own SQL Server tables.
type Migrator interface {
	// Ordered and append-only. Once a migration has shipped, never edit it — add another one.
	Migrations() []Migration
}

// Migration mirrors platform/database.Migration rather than reusing it so that a module's module.go
// can declare its schema without importing the database package, which tools/archlint reserves for
// store/ packages. The kernel does the conversion.
type Migration struct {
	// Unique within the module, and never changes once released; it is the primary key the server
	// uses to decide what has already been applied.
	Name string

	// One or more statements, run as a single T-SQL batch.
	SQL string
}

// Implemented by modules that store documents in Mongo.
type Indexer interface {
	// Creating an index that already exists is a no-op, so this list is declarative: state what
	// should be true, not what changed.
	Indexes() []Index
}

// Index mirrors platform/mongodb.Index for the same reason Migration mirrors its database
// counterpart.
type Index struct {
	// Must start with the module's ID and an underscore. The kernel rejects anything else, which is
	// the one namespacing rule here that a machine can check.
	Collection string

	// Always set it.
	Name string

	Unique bool

	// The indexed fields, in order.
	Keys []IndexKey

	// Turns this into a TTL index: Mongo deletes a document once the indexed date is that far in the
	// past. Zero means an ordinary index. Mongo honours it only on an index over a single date
	// field, so a retention index is always its own index rather than a flag on the compound one a
	// read uses.
	ExpireAfter time.Duration
}

type IndexKey struct {
	Field      string
	Descending bool
}

// Start is the earliest point at which Use[T] is safe.
type Starter interface {
	Start(ctx context.Context, k *Kernel) error
}

// Finalizer is implemented by modules whose work depends on what every other module ended up
// CONTRIBUTING — its routes, its declarations — rather than on the services it published.
//
// Start is not early enough for that and cannot be: it runs module by module, so a module that
// asked during its own Start would see whatever the ones after it had not done yet. Finalize runs
// after every Start has returned, which makes questions like "which modules gate on their own
// access permission" and "which modules own a screen" answerable at all.
//
// It is the last stage before the server serves, so it is also the right place to refuse a
// composition that does not add up.
type Finalizer interface {
	Finalize(ctx context.Context, k *Kernel) error
}

// Stop runs in reverse registration order and is given a context with a deadline, so respect
// cancellation rather than blocking shutdown.
type Stopper interface {
	Stop(ctx context.Context) error
}

// RouteContributor keeps the kernel aware that modules contribute something mountable, and ignorant
// of what any particular one contributes.
type RouteContributor interface {
	// Called once, after every module has started, so resolving another module's service with Use
	// here is safe. The kernel is passed rather than captured because handlers need k.RPC, and a
	// module that stashed the kernel during Start would be holding it for the life of the process
	// to read one field.
	Routes(k *Kernel) []Route
}

// Route is one endpoint a module serves.
type Route struct {
	// MANDATORY: the zero value is not a policy but the absence of one, and Kernel.Routes refuses to
	// collect a route that still carries it. Forgetting is a failed boot rather than an open
	// endpoint.
	Policy RoutePolicy

	// A net/http ServeMux pattern. Connect's generated constructors return exactly this pair — a
	// path prefix ending in a slash, and a handler — so an RPC service is one Route with no
	// adapting, and a plain HTTP endpoint uses the same struct with a pattern of its own.
	Pattern string

	Handler http.Handler

	// A module never sets this: Routes stamps it over whatever was there, from the module it is
	// currently asking. This field decides which access policy a request is checked against, and a
	// value a module could choose for itself is a permission a module could grant itself.
	//
	// The kernel does not read it. internal/app/server uses it to decide which routes to gate, which
	// is the only reason it exists.
	Module string
}
