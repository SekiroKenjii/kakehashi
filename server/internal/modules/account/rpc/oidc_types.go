package rpc

import (
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/zitadel/oidc/v3/pkg/oidc"
	"github.com/zitadel/oidc/v3/pkg/op"
)

// authRequest adapts a stored authorization onto op.AuthRequest. Everything is a projection of
// the row; the wrapper holds no state of its own.
type authRequest struct {
	row domain.AuthRequest
}

func (a *authRequest) GetID() string          { return a.row.ID }
func (a *authRequest) GetACR() string         { return "" }
func (a *authRequest) GetClientID() string    { return a.row.ClientID }
func (a *authRequest) GetNonce() string       { return a.row.Nonce }
func (a *authRequest) GetRedirectURI() string { return a.row.RedirectURI }
func (a *authRequest) GetScopes() []string    { return a.row.Scopes }
func (a *authRequest) GetState() string       { return a.row.State }
func (a *authRequest) GetSubject() string     { return a.row.Subject }
func (a *authRequest) Done() bool             { return a.row.Done }
func (a *authRequest) GetAuthTime() time.Time { return a.row.AuthTime }

// GetAMR reports how the user authenticated. The login page checks a password; there is nothing
// else yet, so the answer is static.
func (a *authRequest) GetAMR() []string { return []string{"pwd"} }

// GetAudience names who the issued tokens are for: the requesting client.
func (a *authRequest) GetAudience() []string { return []string{a.row.ClientID} }

func (a *authRequest) GetResponseType() oidc.ResponseType {
	return oidc.ResponseType(a.row.ResponseType)
}

// GetResponseMode is empty: the default mode for a response type is always what the desktop
// client expects, and offering others is surface without a caller.
func (a *authRequest) GetResponseMode() oidc.ResponseMode { return "" }

func (a *authRequest) GetCodeChallenge() *oidc.CodeChallenge {
	if a.row.CodeChallenge == "" {
		return nil
	}
	method := oidc.CodeChallengeMethodPlain
	if a.row.CodeChallengeMethod == string(oidc.CodeChallengeMethodS256) {
		method = oidc.CodeChallengeMethodS256
	}
	return &oidc.CodeChallenge{Challenge: a.row.CodeChallenge, Method: method}
}

var _ op.AuthRequest = (*authRequest)(nil)

// refreshTokenRequest adapts a stored token row onto op.RefreshTokenRequest for the
// refresh_token grant.
type refreshTokenRequest struct {
	row domain.IssuedToken
}

func (r *refreshTokenRequest) GetAMR() []string       { return []string{"pwd"} }
func (r *refreshTokenRequest) GetAudience() []string  { return r.row.Audience }
func (r *refreshTokenRequest) GetAuthTime() time.Time { return r.row.AuthTime }
func (r *refreshTokenRequest) GetClientID() string    { return r.row.ClientID }
func (r *refreshTokenRequest) GetScopes() []string    { return r.row.Scopes }
func (r *refreshTokenRequest) GetSubject() string     { return r.row.AccountID }
func (r *refreshTokenRequest) SetCurrentScopes(scopes []string) {
	// The client may narrow — never widen — the scopes on refresh. op enforces the narrowing;
	// this just records the result so the new token is issued with it.
	r.row.Scopes = scopes
}

var _ op.RefreshTokenRequest = (*refreshTokenRequest)(nil)

// client is the desktop application, described the way op wants it.
//
// There is exactly one, configured rather than stored: a boilerplate has one first-party client,
// and a table plus registration UI for it would be shape without users. When a second client
// appears, this struct is the thing that becomes a row.
type client struct {
	id           string
	redirectURIs []string
	devMode      bool
}

func (c *client) GetID() string                    { return c.id }
func (c *client) RedirectURIs() []string           { return c.redirectURIs }
func (c *client) PostLogoutRedirectURIs() []string { return c.redirectURIs }

// ApplicationType is native: a desktop app using a loopback redirect, per RFC 8252.
func (c *client) ApplicationType() op.ApplicationType { return op.ApplicationTypeNative }

// AuthMethod is none: a public client cannot keep a secret, so it does not get one. PKCE is what
// binds the code to the caller instead.
func (c *client) AuthMethod() oidc.AuthMethod { return oidc.AuthMethodNone }

func (c *client) ResponseTypes() []oidc.ResponseType {
	return []oidc.ResponseType{oidc.ResponseTypeCode}
}

func (c *client) GrantTypes() []oidc.GrantType {
	return []oidc.GrantType{oidc.GrantTypeCode, oidc.GrantTypeRefreshToken}
}

func (c *client) LoginURL(id string) string {
	return "/account/browser/sign-in?authRequestID=" + id
}

// AccessTokenType is JWT so resource servers — including this process's own Connect handlers —
// can verify tokens against the JWKS without a database round trip per request.
func (c *client) AccessTokenType() op.AccessTokenType { return op.AccessTokenTypeJWT }

func (c *client) IDTokenLifetime() time.Duration { return time.Hour }

// DevMode skips the redirect-URI scheme checks, which is what allows plain http loopback
// redirects when the issuer itself is plain http. It follows the issuer's scheme so a production
// deployment (https issuer) gets the strict checks without anyone remembering to flip it.
func (c *client) DevMode() bool { return c.devMode }

func (c *client) RestrictAdditionalIdTokenScopes() func(scopes []string) []string {
	return func(scopes []string) []string { return scopes }
}

func (c *client) RestrictAdditionalAccessTokenScopes() func(scopes []string) []string {
	return func(scopes []string) []string { return scopes }
}

func (c *client) IsScopeAllowed(scope string) bool {
	// Everything beyond the standard set must be listed here; "roles" is what the desktop client
	// asks for. An unknown scope is dropped by op rather than failing the request.
	return scope == scopeRoles
}

// IDTokenUserinfoClaimsAssertion puts the userinfo claims straight into the ID token, which is
// what lets the client greet the user by name without a second request during sign-in.
func (c *client) IDTokenUserinfoClaimsAssertion() bool { return true }

func (c *client) ClockSkew() time.Duration { return 0 }

var _ op.Client = (*client)(nil)
