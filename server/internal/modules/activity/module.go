// Package activity is the MongoDB reference module, and the reference for one module reacting to
// another's facts without a reference between them.
//
// What it demonstrates that notes does not:
//
//	a document store with no migrations, whose schema management is an index list;
//	the activity_ collection prefix, which EnsureIndexes checks at boot;
//	a subscriber that turns another module's api events into its own records.
//
// Its value is the seam rather than the feature. The account module's /account/security-events
// endpoint already satisfies the literal test — sign in on one machine, open the account page on
// another, the event is there. This module reaches that result down a different path, using a fact
// that is already published instead of inventing a synthetic one to demonstrate it with. Said out
// loud so the next reader does not over-build it.
//
// Two things about it that are easy to get wrong:
//
// The feed is per account. Every read is scoped to the caller's own subject and there is no
// parameter a caller can set. A global feed would require an authorization decision, and this
// module has no basis on which to make one.
//
// Mounting an Indexer makes this module's Mongo health a boot gate for the whole process: the
// kernel aborts Boot on the first index failure, so a Mongo that is up but cannot build an index
// stops the server serving authentication. The compile-time asymmetry — deleting this directory
// costs one line in main.go and breaks nothing — does not hold at boot, and someone will otherwise
// rely on it.
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

// Indexes hands the kernel this module's index list. The kernel creates whatever is missing, after
// every module has registered and before any module starts.
//
// The loop converts between two identical-looking types on purpose: returning the store's own type
// would mean naming platform/mongodb here, and archlint reserves it for store/. The store itself
// is constructible from this file only because its type is never named — the same move
// notes/module.go makes with the SQL handle.
func (m *Module) Indexes() []app.Index {
	src := store.Indexes()

	out := make([]app.Index, 0, len(src))
	for _, ix := range src {
		keys := make([]app.IndexKey, 0, len(ix.Keys))
		for _, k := range ix.Keys {
			keys = append(keys, app.IndexKey{Field: k.Field, Descending: k.Descending})
		}
		out = append(out, app.Index{
			Collection: ix.Collection,
			Name:       ix.Name,
			Unique:     ix.Unique,
			Keys:       keys,
		})
	}
	return out
}

// Routes contributes the RPC service.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)

	// The ordinary case: gated on activity.access.
	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.ModuleAccess()},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Indexer          = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)
