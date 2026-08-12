// Package authz decides who may do what, and to which rows.
//
// Enforcement happens once, in the kernel: this module publishes a platform port the mux resolves,
// so no other module knows it exists and none of them checks anything.
//
// Textbook RBAC — accounts hold roles, roles hold permissions — with one column that is not: every
// grant carries a scope. That row-level half lives on the grant because a permission and the rows
// it reaches are one decision, and splitting them across two systems is how they drift.
//
// The catalogue is declared, not stored. Modules say which permissions they enforce and boot
// reconciles the table to match, so a permission no module claims cannot survive as a row that
// looks grantable and grants nothing.
//
// Grants are resolved per request, never from the token. An access token lives minutes; a
// permission revoked a minute ago must not keep working for the rest of them.
//
// Its own routes are never gated, and neither are health's or account's: a module that answers
// "what may I do" cannot require permission to answer, and signing in cannot require a permission
// you can only have after signing in. That set is named in cmd/server/main.go, because a module
// that could exempt itself could opt out of access control by editing its own file.
package authz

import (
	"context"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

type Module struct {
	svc *service.Service

	// Assembled at Finalize, from the modules implementing authzapi.Catalogue plus one .access per
	// module that gates a route on it.
	declared []domain.Permission
}

// Asks for nothing: what this build is made of is a question the kernel can already answer, and a
// list passed in here was a second copy of the mount list to keep in step.
func New() *Module {
	return &Module{}
}

// ID namespaces the module's SQL schema (authz.*) and its configuration keys.
func (m *Module) ID() string { return "authz" }

func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(store.New(k.SQL), nil, nil)

	app.Provide[authzapi.Service](k, m.svc)
	app.Provide[auth.Permissions](k, m.svc)
	return nil
}

func (m *Module) Start(_ context.Context, k *app.Kernel) error {
	m.svc.WithAccounts(app.Use[accountapi.Service](k))
	return nil
}

// In Finalize rather than Start because the catalogue depends on the route table — which modules
// gate on their own access permission — and no module's routes are all collected until every
// module has started.
func (m *Module) Finalize(ctx context.Context, k *app.Kernel) error {
	m.declared = m.catalogue(k)
	if err := m.svc.Reconcile(ctx, m.declared); err != nil {
		return err
	}

	// The first administrator cannot be made by the product — the screen that would grant a role
	// needs a role to reach — so it is made by configuration.
	admin := k.Cfg.Module(m.ID()).String("BOOTSTRAP_ADMIN", "")
	if admin == "" {
		k.Log.WarnContext(ctx,
			"KAKEHASHI_AUTHZ_BOOTSTRAP_ADMIN is not set; no account holds the Admin role, so "+
				"every gated module refuses everyone. Set it to an existing account's email.")
		return nil
	}
	return m.svc.BootstrapAdmin(ctx, admin)
}

// Two services, and the split between them is the security decision. The caller's own view is open
// to any signed-in caller: a module that answers "what may I do" cannot require permission to
// answer, or a client has no way to draw the locks. The administrator's surface is wrapped once,
// here, so every procedure added to it later inherits the check without anyone remembering to.
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

// One .access per module that actually gates a route on it, so "may this account use this module"
// is an ordinary permission rather than a second mechanism beside the real one. Asking the kernel
// deletes a copy of the mount list that only a test kept honest — and asking for the modules that
// gate rather than the modules that exist fixes worse: the old version minted a grantable,
// official-looking permission for every module whose routes check something else entirely, which
// an administrator could spend a morning granting to no effect.
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
