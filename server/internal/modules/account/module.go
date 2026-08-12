// Package account makes the server its own OpenID Connect provider, and serves the account
// management around it.
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

type Module struct {
	store *store.SQLServer
	svc   *service.Service
	wire  *rpc.Wire
}

func New() *Module { return &Module{} }

// The ID doubles as the SQL schema name (account.*) and the configuration prefix
// (KAKEHASHI_ACCOUNT_*). It is "account" rather than "identity" because IDENTITY is a reserved
// word in T-SQL.
func (m *Module) ID() string { return "account" }

func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

func (m *Module) Register(k *app.Kernel) error {
	m.store = store.New(k.SQL)
	m.svc = service.New(m.store, k.Bus, nil, nil)

	app.Provide[accountapi.Service](k, m.svc)
	return nil
}

// The OpenID Connect provider needs the database, so it cannot be assembled at Register.
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

	// Publishing under the platform contract lets the mux authenticate requests without importing
	// this module — the reason auth.Verifier lives in the platform.
	app.Provide[auth.Verifier](k, wire.Verifier)

	if seedEmail != "" && seedPassword != "" {
		if err := m.seed(ctx, seedEmail, seedName, seedPassword); err != nil {
			return fmt.Errorf("seed account: %w", err)
		}
		k.Log.InfoContext(ctx, "seed account ready", "email", seedEmail)
	}

	return nil
}

// The provider, the sign-in pages and /account/* are open to whoever the endpoint itself decides:
// OpenID Connect has to answer an anonymous browser, and /account/* is about the caller's own
// record.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewAdminRoute(m.svc, k.RPC)

	// Copied rather than appended in place: m.wire.Routes belongs to the wire, and append would be
	// free to write into its backing array.
	out := make([]app.Route, 0, len(m.wire.Routes)+1)
	out = append(out, m.wire.Routes...)

	// The policy is stated on the route rather than hand-wrapped around the handler: deleting one
	// auth.RequirePermission call — or adding a second admin route and forgetting it — used to open
	// the whole user directory to any signed-in caller. The kernel refuses to boot a route that
	// states no policy.
	return append(out, app.Route{
		Pattern: pattern,
		Handler: handler,
		Policy:  app.Permission(accountapi.PermissionManageUsers),
	})
}

// Idempotent: a changed SEED_PASSWORD does not overwrite the stored one, because the account
// already exists.
//
// It grants no roles. Which roles the account holds is the authorization module's to decide; a seed
// that also wrote roles would be a second source of truth.
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
