// Package navigation decides where a client's destinations sit, and lets an administrator move them.
//
// Which destinations EXIST is a fact about the build: a destination is a compiled page behind a
// permission, and no row in a table can conjure one. Where they are ARRANGED is a fact about the
// deployment, and putting that in code meant renaming a heading needed a release. So code declares,
// the database places, and boot reconciles the two — a destination with no row is seeded from its
// declared defaults, a destination with a row is left completely alone. A version that also
// refreshed the defaults would silently undo every rearrangement on every restart.
//
// Nothing here can affect access. Permissions arrive as part of a destination's declaration, the
// route gate enforces them from the same declaration, and no write on the admin surface reaches
// them. That independence is why the layout is safe to hand to an administrator at runtime: the
// worst a mistake here can do is hide something.
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

// systemGroups are the headings this product ships: seeded once, renamed and reordered by an
// administrator thereafter. Here rather than at the composition root because a heading is the thing
// this module manages, so shipping two is a statement about the module, not the deployment.
var systemGroups = []navigationapi.SystemGroup{
	{ID: "utilities", Title: "Utilities", Order: 10},
	{ID: "administration", Title: "Administration", Order: 20},
}

type Module struct {
	svc *service.Service

	// destinations is collected at Finalize and empty until then: a module's own declaration is the
	// only place that knows what protects its screen, and asking before every module has started
	// would see whichever ones happened to start first.
	destinations []navigationapi.Destination
}

// New asks for nothing: every screen is declared by the module that owns it.
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

func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(store.New(k.SQL), nil)
	return nil
}

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

	// Resolved here because Finalize is the first point at which another module's service is
	// guaranteed to exist. Optional: without an authorization module there are no roles, and
	// PreviewLayout says so rather than the boot failing over a screen nobody in that build can
	// reach anyway.
	if grants, ok := app.TryUse[authzapi.Service](k); ok {
		m.svc.WithRoleGrants(grants)
	}

	return m.svc.Reconcile(ctx, systemGroups)
}

// collect checks the three things that would otherwise fail quietly, months later, as a screen
// nobody can reach. A boot that refuses is the right answer to all three: each is a mistake in a
// composition somebody just changed, so the feedback is worth having while they are still looking
// at it.
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
			// Stamped, not claimed: it decides which permission applies when the destination names
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

			// The one that is easy to get wrong and impossible to notice: a destination declaring
			// no permission, owned by a module whose routes are not gated on its own access
			// permission, falls back to a key nobody holds. The row is drawn disabled for
			// everybody, forever, and looks like a permissions bug rather than a declaration that
			// never made sense.
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

// The split between the two services is the security decision. The caller's own pane is open to any
// signed-in caller, because a client cannot draw a locked door until it knows the door is there.
// The layout surface is wrapped once, here, so every procedure added later inherits the check.
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
