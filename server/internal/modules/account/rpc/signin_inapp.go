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

// inAppSignInHandler issues tokens from credentials posted by the app itself, with no browser
// involved. The tokens come from the provider's own op.CreateTokenResponse, so both sign-in modes
// issue identical tokens: docs/adr/0007-in-app-sign-in-alongside-browser-oidc.md
//
// The moment Auth:Authority points at an external IdP, the client must switch back to browser
// mode — real IdPs refuse password grants because they defeat MFA.
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

	// A synthetic op.IDTokenRequest so the provider mints both modes' tokens the same way:
	// docs/adr/0007-in-app-sign-in-alongside-browser-oidc.md.
	request := &authRequest{row: domain.AuthRequest{
		ID:       session.ID,
		ClientID: h.client.id,
		Subject:  account.ID,
		Scopes:   scopes,

		// Load-bearing: op grants a refresh token only for offline_access plus a code response
		// type plus refresh_token in the client's grants.
		ResponseType: string(oidc.ResponseTypeCode),
		SessionID:    session.ID,
		Done:         true,
		AuthTime:     time.Now(),
	}}

	// op's own handlers put the issuer on the context; minting outside them means doing it by hand.
	// Without it the JWT ships with no "iss" and every verifier rejects it, including this one.
	ctx := op.ContextWithIssuer(r.Context(), h.issuer)

	// The empty arguments are the authorization code and the refresh token being rotated; this
	// flow has neither. Claiming a code would put a c_hash over something the client never saw.
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
