// Package activity turns facts other modules publish into a per-account feed.
//
// The seam is the point rather than the feature: /account/security-events already answers the same
// question down a shorter path. Do not over-build this one.
//
// The feed is per account. Every read is scoped to the caller's own subject and no parameter widens
// it; a global feed would need an authorization decision this module has no basis to make.
//
// Mounting an Indexer makes this module's Mongo health a boot gate for the whole process: the
// kernel aborts Boot on the first index failure, so a Mongo that is up but cannot build an index
// stops the server serving authentication. Deleting this directory costs one line in main.go and
// breaks nothing at compile time — that asymmetry does not hold at boot.
package activity

import (
	"log/slog"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/store"
)

type Module struct {
	log *slog.Logger
	svc *service.Service
}

func New() *Module { return &Module{} }

// Namespaces this module's Mongo collections (activity_*) and its configuration keys.
func (m *Module) ID() string { return "activity" }

// The logger is captured because the subscription closures have nowhere else to report a write that
// failed: the bus hands a handler no way to return an error.
func (m *Module) Register(k *app.Kernel) error {
	m.log = k.Log
	m.svc = service.New(store.New(k.Mongo), nil)

	app.Provide[activityapi.Service](k, m.svc)
	m.subscribe(k)
	return nil
}

// The kernel creates whatever is missing, after every module has registered and before any module
// starts.
//
// The loop converts between two identical-looking types on purpose: returning the store's own type
// would mean naming platform/mongodb here, and archlint reserves it for store/.
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
