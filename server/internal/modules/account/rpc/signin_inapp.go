package rpc

import (
	"net/http"
	"time"

	"github.com/zitadel/oidc/v3/pkg/oidc"
	"github.com/zitadel/oidc/v3/pkg/op"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// inAppSignInHandler issues tokens from credentials posted by the app itself, with no browser involved.
//
// # Why this exists alongside the browser flow
//
// Authorization Code + PKCE through the system browser is the right answer when the identity
// provider is someone else's — Entra, Okta, Google — because the whole point is that the password
// is typed into *their* page and this application never sees it, and because that is where SSO,
// MFA and conditional access live.
//
// None of that applies when the provider is this very process. A first-party desktop client
// talking to its own backend gains nothing from bouncing through a browser: the password crosses
// the same trust boundary either way, and the user pays for it with a window that steals focus and
// a loopback listener that corporate firewalls dislike. So the default is this endpoint, and the
// browser flow stays mounted for the day the issuer becomes someone else's.
//
// The line to remember: **the moment Auth:Authority points at a real IdP, the client must switch
// back to browser mode.** Entra and friends refuse password grants for most configurations
// precisely because it defeats MFA — and they are right to.
type inAppSignInHandler struct {
	svc      *service.Service
	store    *store.SQLServer
	client   *client
	provider *op.Provider
	issuer   string
	tokenTTL time.Duration
}

type inAppSignInRequest struct {
	Email    string `json:"email"`
	Password string `json:"password"`
}

// inAppSignInResponse is deliberately the shape of an OAuth token response, so a client can hold one
// type for both modes and the refresh path below is the standard endpoint either way.
type inAppSignInResponse struct {
	AccessToken  string `json:"access_token"`
	TokenType    string `json:"token_type"`
	RefreshToken string `json:"refresh_token,omitempty"`
	IDToken      string `json:"id_token,omitempty"`
	ExpiresIn    uint64 `json:"expires_in"`
	Scope        string `json:"scope,omitempty"`
}

// signIn authenticates and returns tokens indistinguishable from the browser flow's.
func (h *inAppSignInHandler) signIn(w http.ResponseWriter, r *http.Request) {
	var body inAppSignInRequest
	if !readJSON(w, r, &body) {
		return
	}

	device, ip := callerFacts(r)

	account, err := h.svc.Authenticate(r.Context(), body.Email, body.Password, device, ip)
	if err != nil {
		writeError(w, err)
		return
	}

	session, err := h.svc.StartSession(r.Context(), account, h.client.id, device, ip)
	if err != nil {
		writeError(w, err)
		return
	}

	// The same scopes the browser flow's client asks for. Requesting them per sign-in would be a
	// knob with one setting.
	scopes := []string{
		"openid", "profile", "email", "offline_access", scopeRoles,
	}

	// A synthetic authorization, already authenticated. op's token builder wants an
	// op.IDTokenRequest, and everything it asks for is known here — which is the whole reason this
	// endpoint is short: the token minting, signing and claim assembly are the provider's, not
	// ours, so the two sign-in modes cannot drift into issuing different tokens.
	//
	// ResponseType is "code" and it is not decoration. op decides whether an exchange earns a
	// refresh token by asking three questions of the request — offline_access in the scopes, a
	// response type of code, and refresh_token among the client's grants — and a request that
	// answers no to any of them gets an access token alone. Without this field the client would be
	// back at the sign-in form every time the access token aged out, which for a desktop app is
	// every ten minutes.
	request := &authRequest{row: domain.AuthRequest{
		ID:           session.ID,
		ClientID:     h.client.id,
		Subject:      account.ID,
		Scopes:       scopes,
		ResponseType: string(oidc.ResponseTypeCode),
		SessionID:    session.ID,
		Done:         true,
		AuthTime:     time.Now(),
	}}

	// The issuer normally rides in on the context that op's own handlers build. Minting a token
	// outside them means putting it there by hand — without it the JWT ships with no "iss" claim
	// and every verifier, including this server's own, rejects it as not ours.
	ctx := op.ContextWithIssuer(r.Context(), h.issuer)

	// The empty last two arguments are the authorization code and the refresh token being rotated.
	// This flow has neither: no code was ever issued, so claiming one would put a c_hash in the ID
	// token that hashes something the client never saw, and there is no prior refresh token
	// because this is the first exchange.
	response, err := op.CreateTokenResponse(
		ctx, request, h.client, h.provider, true, "", "")
	if err != nil {
		writeError(w, errs.Internalf(err, "issue tokens"))
		return
	}

	writeJSON(w, inAppSignInResponse{
		AccessToken:  response.AccessToken,
		TokenType:    response.TokenType,
		RefreshToken: response.RefreshToken,
		IDToken:      response.IDToken,
		ExpiresIn:    response.ExpiresIn,
		Scope:        response.Scope.String(),
	})
}

// signOut ends the calling session. The browser flow has /end_session for this; an app that never
// opened a browser has no cookie to clear, so it just needs its session gone.
func (h *inAppSignInHandler) signOut(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	if subject.SessionID == "" {
		// A token minted before sessions were stamped into claims. Nothing to revoke precisely,
		// and revoking everything would sign the user out of their other devices without asking.
		w.WriteHeader(http.StatusNoContent)
		return
	}

	if err := h.svc.SignOut(r.Context(), subject.ID, subject.SessionID); err != nil {
		writeError(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
