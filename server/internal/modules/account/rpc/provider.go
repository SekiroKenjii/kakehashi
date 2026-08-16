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

	"__GO_MODULE__/server/internal/app"
	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	"__GO_MODULE__/server/internal/modules/account/service"
	"__GO_MODULE__/server/internal/modules/account/store"
	"__GO_MODULE__/server/internal/platform/auth"
)

// Options is everything the wire layer needs to stand up the provider.
type Options struct {
	// Issuer is the externally reachable origin — cfg.PublicURL. Tokens carry it, discovery
	// advertises it, and a client that dialled anything else rejects them.
	Issuer string

	// ClientID and RedirectURIs describe the one registered client, the desktop app.
	ClientID     string
	RedirectURIs []string

	// CryptoSecret keys op's internal encryption of codes and refresh-token payloads. Any stable
	// string; it is hashed to the 32 bytes op wants. Changing it invalidates in-flight codes.
	CryptoSecret string

	// AccessTokenTTL bounds how long a stolen access token works — and how long a revoked
	// session's token keeps verifying, since verification is local. Minutes, not hours.
	AccessTokenTTL time.Duration

	Logger *slog.Logger
}

// Wire is the account module's assembled HTTP surface.
type Wire struct {
	// Verifier is what the rest of the server uses to authenticate callers. The module publishes
	// it on the kernel under auth.Verifier.
	Verifier auth.Verifier

	// Routes is every endpoint this module serves, ready for the kernel's mux.
	Routes []app.Route
}

// New builds the OpenID Connect provider, the sign-in handlers and the account endpoints.
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
		// A plain-http issuer is a development stack, and op refuses to build one unless told. In
		// production the reverse proxy terminates TLS, so this branch never runs.
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
		// The policy split is this module's whole security surface, and requireSubject inside each
		// handler is the inner guarantee: docs/adr/0001-per-route-permission-policy.md.
		Routes: []app.Route{
			// The catch-all: discovery, /authorize, /oauth/token, /userinfo, /keys, /end_session and
			// /revoke inherit Public, each authenticating by its own protocol rather than a bearer.
			{Pattern: "/", Handler: provider, Policy: app.Public()},

			// In-app sign-in, the default for a first-party desktop client: refresh and revocation
			// stay on the standard OAuth endpoints, so both modes share one token lifecycle.
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

			// The browser flow, still mounted: it is what you switch to the day the issuer is
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

// Interface conformance that the compiler cannot otherwise see from here.
var _ accountapi.Service = (*service.Service)(nil)
