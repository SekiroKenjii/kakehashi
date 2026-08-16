// Package authz decides who may do what, and to which rows.
//
// It is the authorization reference: a module that publishes a platform port the kernel resolves,
// so enforcement happens once, for every route, at the one place in the server that sees every
// request. No other module knows this one exists, and none of them checks anything.
//
// The model is textbook RBAC — accounts hold roles, roles hold permissions — with one column that
// is not textbook: every grant carries a scope. That is the row-level half, and it lives on the
// grant because a permission and the rows it reaches are one decision. Splitting them across two
// systems is how they drift.
//
// Three things about it that are easy to get wrong:
//
// The catalogue is declared, not stored. Modules say which permissions they enforce and boot
// reconciles the table to match, so a permission no module claims cannot survive as a row that
// looks grantable and grants nothing.
//
// Grants are resolved per request, never from the token. An access token lives minutes; a
// permission revoked a minute ago must not keep working for the rest of them.
//
// Its own routes are never gated, and neither are health's or account's. A module that answers
// "what may I do" cannot require permission to answer, and signing in cannot require a permission
// you can only have after signing in. That set is named in cmd/server/main.go, because a module
// that could exempt itself would be a module that could opt out of access control by editing its
// own file.
package authz

import (
	"context"

	"__GO_MODULE__/server/internal/app"
	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	authzapi "__GO_MODULE__/server/internal/modules/authz/api"
	"__GO_MODULE__/server/internal/modules/authz/domain"
	"__GO_MODULE__/server/internal/modules/authz/rpc"
	"__GO_MODULE__/server/internal/modules/authz/service"
	"__GO_MODULE__/server/internal/modules/authz/store"
	"__GO_MODULE__/server/internal/platform/auth"
)

// Module is the authorization feature.
type Module struct {
	svc *service.Service

	// declared is the catalogue, assembled at Finalize from the modules that implement
	// authzapi.Catalogue plus one .access permission per module that actually gates a route on it.
	declared []domain.Permission
}

// New returns the module. It asks for nothing: what this build is made of is a question the kernel
// can already answer, and a list passed in here was a second copy of the mount list to keep in step.
func New() *Module {
	return &Module{}
}

// ID namespaces the module's SQL schema (authz.*) and its configuration keys.
func (m *Module) ID() string { return "authz" }

// Migrations hands the kernel this module's schema.
func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

// Register builds the service and publishes both faces of it: the read contract other modules may
// use, and the platform port the mux enforces with.
func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(store.New(k.SQL), nil, nil)

	app.Provide[authzapi.Service](k, m.svc)
	app.Provide[auth.Permissions](k, m.svc)
	return nil
}

// Start resolves what this module needs from other modules.
func (m *Module) Start(_ context.Context, k *app.Kernel) error {
	m.svc.WithAccounts(app.Use[accountapi.Service](k))
	return nil
}

// Finalize assembles the catalogue and reconciles it.
//
// In Finalize rather than Start because the catalogue now depends on the ROUTE TABLE — which
// modules gate on their own access permission — and no module's routes are all collected until
// every module has started.
func (m *Module) Finalize(ctx context.Context, k *app.Kernel) error {
	m.declared = m.catalogue(k)
	if err := m.svc.Reconcile(ctx, m.declared); err != nil {
		return err
	}

	// Without this a fresh deployment locks itself out: every module gated, nobody holding a role,
	// and the screen that grants one needing a role to reach. So configuration makes the first.
	admin := k.Cfg.Module(m.ID()).String("BOOTSTRAP_ADMIN", "")
	if admin == "" {
		k.Log.WarnContext(ctx,
			"__APP_NAME_UPPER___AUTHZ_BOOTSTRAP_ADMIN is not set; no account holds the Admin role, so "+
				"every gated module refuses everyone. Set it to an existing account's email.")
		return nil
	}
	return m.svc.BootstrapAdmin(ctx, admin)
}

// Routes contributes two RPC services, and the split between them is the security decision.
//
// The caller's own view is open to any signed-in caller: a module that answers "what may I do"
// cannot require permission to answer, or a client has no way to draw the locks. The
// administrator's surface is wrapped once, here, so every procedure added to it later inherits the
// check without anyone remembering to — the same argument the route gate makes.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(k.RPC)
	adminPattern, adminHandler := rpc.NewAdminRoute(m.svc, k.RPC)

	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.SignedIn()},
		{
			Pattern: adminPattern,
			Handler: adminHandler,
			Policy:  app.Permission(authzapi.PermissionManageRoles),
		},
	}
}

// catalogue assembles every permission this build enforces: only modules the route table shows
// gating on their own .access are minted a permission
// (docs/adr/0002-permission-catalogue-from-gating-modules.md), plus the finer permissions modules
// declare through authzapi.Catalogue — how a module says what it checks in its own handlers
// without this one having to know.
func (m *Module) catalogue(k *app.Kernel) []domain.Permission {
	access := k.AccessModules()

	out := make([]domain.Permission, 0, len(access))
	for _, id := range access {
		out = append(out, domain.Permission{
			Key:         auth.ModuleAccess(id),
			Name:        "Use " + id,
			Description: "Reach the " + id + " module's endpoints at all",
			Category:    "Module access",
		})
	}

	for _, declared := range app.UseAll[authzapi.Catalogue](k) {
		for _, p := range declared.Permissions() {
			out = append(out, domain.Permission{
				Key:         p.Key,
				Name:        p.Name,
				Description: p.Description,
				Category:    p.Category,
				IsHighRisk:  p.IsHighRisk,
				IsScoped:    p.IsScoped,
			})
		}
	}
	return out
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Migrator         = (*Module)(nil)
	_ app.Starter          = (*Module)(nil)
	_ app.Finalizer        = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)
