// Package account makes the server its own OpenID Connect provider, and serves the account
// management around it.
//
// The module's layers:
//
//	api/      the contract: the account DTOs, the Service surface, the security-event kinds.
//	domain/   Account and its invariants; password hashing lives behind it.
//	store/    persistence, in the account schema. Owns the provider's state too.
//	service/  the use cases: authenticate, sessions, profile, audit trail.
//	rpc/      the wire: the OIDC provider, the sign-in pages, the /account endpoints,
//	          and the auth.Verifier the rest of the server authenticates with.
//	module.go the wiring below.
//
// It is the one place in the repository allowed to import an OpenID Connect library, and
// tools/archlint enforces that.
package account

import (
	"context"
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Module is the account feature: identity, sessions, and the OpenID Connect provider.
type Module struct {
	store *store.SQLServer
	svc   *service.Service
	wire  *rpc.Wire
}

// New returns the module, ready to be mounted on the kernel.
func New() *Module { return &Module{} }

// ID namespaces the module's schema (account.*) and its configuration keys (KAKEHASHI_ACCOUNT_*).
// It is "account" rather than "identity" because the ID doubles as the SQL schema name, and
// IDENTITY is a reserved word in T-SQL.
func (m *Module) ID() string { return "account" }

// Migrations hands the kernel this module's schema.
func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

// Register builds the service and publishes the module's contracts.
func (m *Module) Register(k *app.Kernel) error {
	m.store = store.New(k.SQL)
	m.svc = service.New(m.store, k.Bus, nil, nil)

	app.Provide[accountapi.Service](k, m.svc)
	return nil
}

// Start assembles the OpenID Connect provider — which needs the database, so it cannot happen at
// Register — publishes the verifier, and seeds the development account when asked to.
func (m *Module) Start(ctx context.Context, k *app.Kernel) error {
	section := k.Cfg.Module(m.ID())
	options := rpc.Options{
		Issuer:   k.Cfg.PublicURL,
		ClientID: section.String("CLIENT_ID", "kakehashi-desktop"),
		RedirectURIs: splitList(
			section.String("REDIRECT_URIS", "http://127.0.0.1:8765/")),
		CryptoSecret:   section.String("CRYPTO_SECRET", ""),
		AccessTokenTTL: section.Duration("ACCESS_TOKEN_TTL", 10*time.Minute),
		Logger:         k.Log,
	}
	seedEmail := section.String("SEED_EMAIL", "")
	seedPassword := section.String("SEED_PASSWORD", "")
	seedName := section.String("SEED_NAME", "Developer")
	if err := section.Err(); err != nil {
		return err
	}

	if options.CryptoSecret == "" {
		// Boot anyway, loudly: a dev stack works with a fixed secret, a production deployment
		// must set its own. Refusing to start would make the boilerplate unrunnable on clone.
		options.CryptoSecret = "kakehashi-dev-crypto-secret"
		k.Log.WarnContext(ctx,
			"KAKEHASHI_ACCOUNT_CRYPTO_SECRET is not set; using the development default. "+
				"Set it before exposing this server to anything.")
	}

	wire, err := rpc.New(ctx, m.store, m.svc, options)
	if err != nil {
		return fmt.Errorf("build oidc provider: %w", err)
	}
	m.wire = wire

	// Publishing under the platform contract is what lets the mux authenticate requests without
	// importing this module — the whole reason auth.Verifier lives in the platform.
	app.Provide[auth.Verifier](k, wire.Verifier)

	if seedEmail != "" && seedPassword != "" {
		if err := m.seed(ctx, seedEmail, seedName, seedPassword); err != nil {
			return fmt.Errorf("seed account: %w", err)
		}
		k.Log.InfoContext(ctx, "seed account ready", "email", seedEmail)
	}

	return nil
}

// Routes contributes the provider, the sign-in pages, the account endpoints, and the
// administrative surface.
//
// The first three are open to whoever the endpoint itself decides: OpenID Connect has to answer an
// anonymous browser, and /account/* is about the caller's own record. The fourth is wrapped once,
// here, so every procedure added to it later inherits the check rather than needing somebody to
// remember it.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewAdminRoute(m.svc, k.RPC)

	// Copied rather than appended in place: m.wire.Routes belongs to the wire, and append would be
	// free to write into its backing array.
	out := make([]app.Route, 0, len(m.wire.Routes)+1)
	out = append(out, m.wire.Routes...)

	// The administrative surface, and the reason the policy lives on the route rather than in a
	// wrapper here. This used to be auth.RequirePermission(...) written by hand around the handler,
	// which meant deleting one call — or adding a second admin route and forgetting it — opened the
	// whole user directory to any signed-in caller, with nothing to catch it. Stated as a policy,
	// the mux applies it and the kernel refuses to boot a route that states nothing.
	return append(out, app.Route{
		Pattern: pattern,
		Handler: handler,
		Policy:  app.Permission(accountapi.PermissionManageUsers),
	})
}

// seed creates the development account when it does not exist yet. Idempotent: booting twice with
// the same configuration is a no-op, and a changed password in the environment does not overwrite
// the stored one — the account exists, so nothing happens.
//
// It grants nothing. Which roles this account holds is the authorization module's to decide, and a
// seed here that also wrote roles would be the second source of truth this refactor exists to
// remove.
func (m *Module) seed(ctx context.Context, email, name, password string) error {
	if _, err := m.store.AccountByEmail(ctx, email); err == nil {
		return nil
	} else if errs.KindOf(err) != errs.NotFound {
		return err
	}

	account, err := domain.NewAccount(uuid.NewString(), email, name, password, time.Now())
	if err != nil {
		return err
	}
	return m.store.InsertAccount(ctx, account)
}

func splitList(raw string) []string {
	var out []string
	for _, item := range strings.Split(raw, ",") {
		if trimmed := strings.TrimSpace(item); trimmed != "" {
			out = append(out, trimmed)
		}
	}
	return out
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Migrator         = (*Module)(nil)
	_ app.Starter          = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)
