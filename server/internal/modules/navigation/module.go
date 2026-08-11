// Package navigation decides where a client's destinations sit, and lets an administrator move them.
//
// It exists because a navigation pane has two owners and they want different things. Which
// destinations EXIST is a fact about the build: a destination is a compiled page behind a permission,
// and no row in a table can conjure one. Where they are ARRANGED is a fact about the deployment:
// which heading a screen sits under, in what order, under what label. The first belongs in code. The
// second belonged in code too, until it meant that renaming a heading needed a release.
//
// So: code declares, the database places, and boot reconciles the two. The reconciliation has one
// rule worth remembering — a destination with no row is seeded from its declared defaults, a
// destination with a row is left completely alone. A version that also refreshed the defaults would
// silently undo every rearrangement on every restart.
//
// What this module deliberately cannot do is affect access. Permissions arrive as part of a
// destination's declaration, the route gate enforces them from the same declaration, and there is no
// write on the admin surface that reaches them. That independence is the reason the layout is safe to
// hand to an administrator at runtime: the worst a mistake here can do is hide something.
//
// Its own read route is ungated, like health's and account's and authz's. A client needs its pane
// before it can draw anything, so an account with no grants must still be able to ask what it may
// see. That set is named in cmd/server/main.go.
package navigation

import (
	"context"
	"fmt"
	"slices"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// systemGroups are the headings this product ships.
//
// Seeded once and renamable thereafter, and here rather than at the composition root because they
// are what this module's own vocabulary is made of: a heading is the thing it manages, and shipping
// two of them is a statement about the module, not about the deployment. An administrator adds,
// renames and reorders them at runtime — which is the entire reason the layout lives in a database.
var systemGroups = []navigationapi.SystemGroup{
	{ID: "utilities", Title: "Utilities", Order: 10},
	{ID: "administration", Title: "Administration", Order: 20},
}

// Module is the navigation feature.
type Module struct {
	svc *service.Service

	// destinations is every screen this build has, collected at Finalize from the modules that
	// declare one. Empty until then: a module's own declaration is the only place that knows what
	// protects its screen, and asking before every module has started would see whichever ones
	// happened to start first.
	destinations []navigationapi.Destination
}

// New returns the module. It asks for nothing: every screen is declared by the module that owns it.
func New() *Module {
	return &Module{}
}

// ID namespaces the module's SQL schema (navigation.*) and its configuration keys.
func (m *Module) ID() string { return "navigation" }

func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

// Register builds the service.
func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(store.New(k.SQL), nil)
	return nil
}

// Finalize collects every module's screens, refuses a composition that does not add up, and
// reconciles the stored layout against it.
//
// In Finalize rather than Start because it asks two questions no module can answer during its own
// Start: what every other module declared, and which modules gate a route on their own access
// permission. Both are only complete once every Start has returned.
func (m *Module) Finalize(ctx context.Context, k *app.Kernel) error {
	declared, err := collect(k)
	if err != nil {
		return err
	}

	m.destinations = declared
	m.svc.WithDestinations(declared...)

	// Optional, and resolved here for the same reason the declarations are: Finalize is the first
	// point at which another module's service is guaranteed to exist. Without an authorization module
	// there are no roles, and PreviewLayout says so rather than the boot failing over a screen nobody
	// in that build can reach anyway.
	if grants, ok := app.TryUse[authzapi.Service](k); ok {
		m.svc.WithRoleGrants(grants)
	}

	return m.svc.Reconcile(ctx, systemGroups)
}

// collect gathers the declarations and checks the three things that would otherwise fail quietly,
// months later, as a screen nobody can reach.
//
// A boot that refuses is the right answer to all three: each is a mistake in the composition, and a
// composition is a thing somebody just changed, so the feedback is worth having while they are
// still looking at it.
func collect(k *app.Kernel) ([]navigationapi.Destination, error) {
	gated := k.AccessModules()

	var out []navigationapi.Destination
	seen := make(map[string]string)

	for _, module := range k.Modules() {
		contributor, ok := module.(navigationapi.Contributor)
		if !ok {
			continue
		}

		for _, d := range contributor.NavigationDestinations() {
			// Stamped, not claimed. It decides which permission applies when the destination names
			// none, and a module that could name another's would be granting itself that module's
			// treatment.
			d.ModuleID = module.ID()

			if owner, dup := seen[d.ID]; dup {
				return nil, fmt.Errorf(
					"destination %q is declared by both %q and %q", d.ID, owner, d.ModuleID)
			}
			seen[d.ID] = d.ModuleID

			if d.DefaultGroup != "" && !slices.ContainsFunc(
				systemGroups,
				func(g navigationapi.SystemGroup) bool { return g.ID == d.DefaultGroup },
			) {
				return nil, fmt.Errorf(
					"destination %q seeds into heading %q, which this build does not ship",
					d.ID, d.DefaultGroup)
			}

			// The one that is easy to get wrong and impossible to notice: a destination owned by a
			// module whose routes are not gated on its own access permission, declaring no
			// permission of its own, falls back to a key nobody holds. The row is drawn disabled
			// for everybody, forever, and looks like a permissions bug rather than a declaration
			// that never made sense.
			if d.Permission == "" && !slices.Contains(gated, d.ModuleID) {
				return nil, fmt.Errorf(
					"destination %q names no permission, and its module %q does not gate any route "+
						"on %s, so nothing could ever unlock it",
					d.ID, d.ModuleID, auth.ModuleAccess(d.ModuleID))
			}

			out = append(out, d)
		}
	}
	return out, nil
}

// Routes contributes two services, and the split between them is the security decision.
//
// The caller's own pane is open to any signed-in caller, because a client cannot draw a locked door
// until it knows the door is there. The layout surface is wrapped once, here, so every procedure
// added to it later inherits the check — the same argument the route gate makes.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)
	adminPattern, adminHandler := rpc.NewAdminRoute(m.svc, k.RPC)

	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.SignedIn()},
		{
			Pattern: adminPattern,
			Handler: adminHandler,
			Policy:  app.Permission(navigationapi.PermissionManageNavigation),
		},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Migrator         = (*Module)(nil)
	_ app.Finalizer        = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)
