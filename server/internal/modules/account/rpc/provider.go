// Package rpc is the account module's wire layer: the OpenID Connect provider, both sign-in
// paths, and the JSON endpoints the desktop client's account page calls.
//
// It is the only package in the repository allowed to import an OpenID Connect library —
// tools/archlint rule 7 — and within the module it is the only layer that knows HTTP exists.
package rpc

import (
	"context"
	"crypto/sha256"
	"log/slog"
	"net/http"
	"strings"
	"time"

	"github.com/zitadel/oidc/v3/pkg/op"
	"golang.org/x/text/language"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

type Options struct {
	// The externally reachable origin — cfg.PublicURL. Tokens carry it, discovery advertises it,
	// and a client that dialled anything else rejects them.
	Issuer string

	// The one registered client, the desktop app.
	ClientID     string
	RedirectURIs []string

	// Keys op's internal encryption of codes and refresh-token payloads. Any stable string; it is
	// hashed to the 32 bytes op wants. Changing it invalidates in-flight codes.
	CryptoSecret string

	// Bounds how long a stolen access token works — and how long a revoked session's token keeps
	// verifying, since verification is local. Minutes, not hours.
	AccessTokenTTL time.Duration

	Logger *slog.Logger
}

type Wire struct {
	Verifier auth.Verifier

	Routes []app.Route
}

func New(
	ctx context.Context, st *store.SQLServer, svc *service.Service, opts Options,
) (*Wire, error) {
	signer, err := loadOrCreateSigningKey(ctx, st)
	if err != nil {
		return nil, err
	}

	insecure := strings.HasPrefix(opts.Issuer, "http://")
	desktop := &client{
		id:           opts.ClientID,
		redirectURIs: opts.RedirectURIs,
		devMode:      insecure,
	}

	storage := newStorage(st, desktop, opts.AccessTokenTTL, signer)

	providerOptions := []op.Option{op.WithLogger(opts.Logger)}
	if insecure {
		// A plain-http issuer is a development stack. op refuses to build one unless told the
		// operator knows; in production the reverse proxy terminates TLS and the issuer is https,
		// so this branch never runs there.
		providerOptions = append(providerOptions, op.WithAllowInsecure())
	}

	provider, err := op.NewOpenIDProvider(
		opts.Issuer,
		&op.Config{
			// op encrypts authorization codes with this; deriving it by hash means any stable
			// secret works and none of them is ever the key verbatim.
			CryptoKey:             sha256.Sum256([]byte(opts.CryptoSecret)),
			CodeMethodS256:        true,
			GrantTypeRefreshToken: true,
			SupportedUILocales:    []language.Tag{language.English},
			SupportedScopes: []string{
				"openid", "profile", "email", "phone", "offline_access", scopeRoles,
			},
		},
		storage,
		providerOptions...,
	)
	if err != nil {
		return nil, err
	}

	browserSignIn := &browserSignInHandler{
		svc:         svc,
		clientID:    opts.ClientID,
		callbackURL: op.AuthCallbackURL(provider),
	}
	account := &accountHandler{svc: svc}
	inAppSignIn := &inAppSignInHandler{
		svc:      svc,
		store:    st,
		client:   desktop,
		provider: provider,
		issuer:   opts.Issuer,
		tokenTTL: opts.AccessTokenTTL,
	}

	return &Wire{
		Verifier: newVerifier(opts.Issuer, &publicKey{id: signer.id, key: &signer.key.PublicKey}),
		// Every route names its policy, and the split is the whole security surface of this module.
		// Public is only what has to answer before anybody can sign in. Everything about somebody's
		// own account requires a verified caller and no permission — a permission guarding your own
		// profile is one an administrator could take away, leaving an account that can sign in and
		// then do nothing.
		//
		// The handlers behind the signed-in routes still call requireSubject, because they need the
		// Subject's value and answer in this surface's own JSON error shape. The policy is the
		// outer guarantee; requireSubject is the inner one that produces the answer.
		Routes: []app.Route{
			// The catch-all: discovery, /authorize, /oauth/token, /userinfo, /keys, /end_session
			// and /revoke all live under it, and net/http's specificity rules keep every other
			// module's more specific patterns on top of it.
			//
			// Everything behind it inherits Public, which is correct for OpenID Connect — every
			// endpoint under it either serves an anonymous browser or authenticates by its own
			// protocol, with the client secret or the PKCE verifier rather than with this server's
			// bearer token.
			{Pattern: "/", Handler: provider, Policy: app.Public()},

			// The default for a first-party desktop client against its own backend. Refresh and
			// revocation stay on the standard OAuth endpoints, so both sign-in modes share one
			// token lifecycle.
			{
				Pattern: "POST /account/sign-in",
				Handler: http.HandlerFunc(inAppSignIn.signIn),
				Policy:  app.Public(),
			},
			{
				Pattern: "POST /account/sign-out",
				Handler: http.HandlerFunc(inAppSignIn.signOut),
				Policy:  app.SignedIn(),
			},

			// The browser flow is still mounted: it is what you switch to the day the issuer is
			// Entra or Okta rather than this process.
			{
				Pattern: "GET /account/browser/sign-in",
				Handler: http.HandlerFunc(browserSignIn.showForm),
				Policy:  app.Public(),
			},
			{
				Pattern: "POST /account/browser/sign-in",
				Handler: http.HandlerFunc(browserSignIn.submit),
				Policy:  app.Public(),
			},
			{
				Pattern: "GET /account/profile",
				Handler: http.HandlerFunc(account.profile),
				Policy:  app.SignedIn(),
			},
			{
				Pattern: "PUT /account/profile",
				Handler: http.HandlerFunc(account.updateProfile),
				Policy:  app.SignedIn(),
			},
			{
				Pattern: "POST /account/password",
				Handler: http.HandlerFunc(account.changePassword),
				Policy:  app.SignedIn(),
			},
			{
				Pattern: "GET /account/sessions",
				Handler: http.HandlerFunc(account.sessions),
				Policy:  app.SignedIn(),
			},
			{
				Pattern: "DELETE /account/sessions/{id}",
				Handler: http.HandlerFunc(account.revokeSession),
				Policy:  app.SignedIn(),
			},
			{
				Pattern: "POST /account/sessions/revoke-all",
				Handler: http.HandlerFunc(account.revokeAllSessions),
				Policy:  app.SignedIn(),
			},
			{
				Pattern: "GET /account/security-events",
				Handler: http.HandlerFunc(account.securityEvents),
				Policy:  app.SignedIn(),
			},
		},
	}, nil
}

var _ accountapi.Service = (*service.Service)(nil)
