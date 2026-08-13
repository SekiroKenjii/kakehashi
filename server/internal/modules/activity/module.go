// Package activity turns facts published by other modules into a per-account feed. Every read is
// scoped to the caller's own subject: a global feed would require an authorization decision this
// module has no basis to make. It is the MongoDB reference module — no migrations, the store's
// index list is the whole schema management — and subscriptions.go is the only file in the module
// that imports another module.
package activity

import (
	"log/slog"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/store"
)

// Module is the activity feature.
type Module struct {
	log *slog.Logger
	svc *service.Service
}

// New returns the module, ready to be mounted on the kernel.
func New() *Module { return &Module{} }

// ID namespaces the module's Mongo collections (activity_*) and its configuration keys.
func (m *Module) ID() string { return "activity" }

// Register builds the service, publishes it, and subscribes to the facts the feed is made of.
//
// The logger is captured because the subscription closures have nowhere else to report a write
// that failed: the bus hands a handler no way to return an error.
func (m *Module) Register(k *app.Kernel) error {
	m.log = k.Log
	m.svc = service.New(store.New(k.Mongo), nil)

	app.Provide[activityapi.Service](k, m.svc)
	m.subscribe(k)
	return nil
}

// Indexes hands the kernel this module's index list. The kernel creates whatever is missing after
// every module has registered and before any starts. Mounting an Indexer makes this module's Mongo
// health a boot gate: the kernel aborts Boot on the first index failure.
//
// The loop converts between two identical-looking types because returning the store's own type
// would name platform/mongodb here, and archlint reserves that import for store/.
func (m *Module) Indexes() []app.Index {
	src := store.Indexes()

	out := make([]app.Index, 0, len(src))
	for _, ix := range src {
		keys := make([]app.IndexKey, 0, len(ix.Keys))
		for _, k := range ix.Keys {
			keys = append(keys, app.IndexKey{Field: k.Field, Descending: k.Descending})
		}
		out = append(out, app.Index{
			Collection:  ix.Collection,
			Name:        ix.Name,
			Unique:      ix.Unique,
			Keys:        keys,
			ExpireAfter: ix.ExpireAfter,
		})
	}
	return out
}

// Routes contributes the RPC service.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)

	// Gated on activity.access.
	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.ModuleAccess()},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Indexer          = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)
