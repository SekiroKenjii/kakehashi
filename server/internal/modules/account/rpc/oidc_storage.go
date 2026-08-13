package rpc

import (
	"context"
	"fmt"
	"time"

	jose "github.com/go-jose/go-jose/v4"
	"github.com/google/uuid"
	"github.com/zitadel/oidc/v3/pkg/oidc"
	"github.com/zitadel/oidc/v3/pkg/op"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Scopes and claims beyond the OpenID Connect standard set.
const (
	scopeRoles = "roles"

	claimRoles   = "roles"
	claimSession = "sid"
)

// storage implements op.Storage over the module's store.
//
// It is glue, on purpose. Every decision — what a valid password is, when a session exists, which
// events get recorded — lives in domain/ and service/; this file translates op's vocabulary into
// theirs and nothing more.
type storage struct {
	store    *store.SQLServer
	client   *client
	tokenTTL time.Duration

	signer *signingKey
}

func newStorage(
	st *store.SQLServer, c *client, tokenTTL time.Duration, signer *signingKey,
) *storage {
	return &storage{store: st, client: c, tokenTTL: tokenTTL, signer: signer}
}

// CreateAuthRequest stores the parsed /authorize request. userID is empty for the code flow: the
// user has not signed in yet, that is what the browser sign-in page is about to do.
func (s *storage) CreateAuthRequest(
	ctx context.Context, r *oidc.AuthRequest, userID string,
) (op.AuthRequest, error) {
	row := domain.AuthRequest{
		ID:           uuid.NewString(),
		ClientID:     r.ClientID,
		Subject:      userID,
		Scopes:       r.Scopes,
		RedirectURI:  r.RedirectURI,
		ResponseType: string(r.ResponseType),
		Nonce:        r.Nonce,
		State:        r.State,
		CreatedAt:    time.Now(),
	}
	if r.CodeChallenge != "" {
		row.CodeChallenge = r.CodeChallenge
		row.CodeChallengeMethod = string(r.CodeChallengeMethod)
	}

	if err := s.store.InsertAuthRequest(ctx, row); err != nil {
		return nil, err
	}
	return &authRequest{row: row}, nil
}

func (s *storage) AuthRequestByID(ctx context.Context, id string) (op.AuthRequest, error) {
	row, err := s.store.AuthRequestByID(ctx, id)
	if err != nil {
		return nil, err
	}
	return &authRequest{row: row}, nil
}

func (s *storage) AuthRequestByCode(ctx context.Context, code string) (op.AuthRequest, error) {
	row, err := s.store.AuthRequestByCode(ctx, code)
	if err != nil {
		return nil, err
	}
	return &authRequest{row: row}, nil
}

func (s *storage) SaveAuthCode(ctx context.Context, id, code string) error {
	return s.store.SaveAuthCode(ctx, id, code)
}

func (s *storage) DeleteAuthRequest(ctx context.Context, id string) error {
	return s.store.DeleteAuthRequest(ctx, id)
}

// CreateAccessToken issues an access token with no refresh token — the path taken when
// offline_access was not requested.
func (s *storage) CreateAccessToken(
	ctx context.Context, request op.TokenRequest,
) (string, time.Time, error) {
	token, err := s.insertToken(ctx, request, "", "")
	if err != nil {
		return "", time.Time{}, err
	}
	return token.ID, token.ExpiresAt, nil
}

// CreateAccessAndRefreshTokens issues an access token and rotates the refresh token.
//
// On the first exchange currentRefreshToken is empty and a fresh one is minted. On a refresh the
// spent token row is deleted before the new one is written: a refresh token that has been used
// ceases to exist, so a replay of it fails loudly instead of silently minting a second session.
func (s *storage) CreateAccessAndRefreshTokens(
	ctx context.Context, request op.TokenRequest, currentRefreshToken string,
) (string, string, time.Time, error) {
	token, err := s.insertToken(ctx, request, uuid.NewString(), currentRefreshToken)
	if err != nil {
		return "", "", time.Time{}, err
	}
	return token.ID, token.RefreshToken, token.ExpiresAt, nil
}

func (s *storage) TokenRequestByRefreshToken(
	ctx context.Context, refreshToken string,
) (op.RefreshTokenRequest, error) {
	row, err := s.store.TokenByRefresh(ctx, refreshToken)
	if err != nil {
		return nil, op.ErrInvalidRefreshToken
	}

	// The session is being used, even though no user is looking at it. Without this, "last seen"
	// freezes at sign-in for a client that only ever refreshes silently.
	_ = s.store.TouchSession(ctx, row.SessionID, time.Now())

	return &refreshTokenRequest{row: row}, nil
}

// TerminateSession is the end-session endpoint's storage half: the user asked to sign out of this
// client, so their sessions with it end and the cascade takes the tokens.
func (s *storage) TerminateSession(ctx context.Context, userID, clientID string) error {
	return s.store.DeleteSessionsForUserClient(ctx, userID, clientID)
}

// RevokeToken handles RFC 7009 revocation, of either kind of token.
func (s *storage) RevokeToken(
	ctx context.Context, tokenOrTokenID, userID, clientID string,
) *oidc.Error {
	// Access tokens are referenced by id and refresh tokens by value, and nothing distinguishes
	// them here, so try both. Deleting something already gone succeeds: revocation is idempotent.
	if err := s.store.DeleteToken(ctx, tokenOrTokenID); err != nil {
		return oidc.ErrServerError().WithDescription("could not revoke token")
	}
	if err := s.store.DeleteTokenByRefresh(ctx, tokenOrTokenID); err != nil {
		return oidc.ErrServerError().WithDescription("could not revoke token")
	}
	return nil
}

func (s *storage) GetRefreshTokenInfo(
	ctx context.Context, clientID, token string,
) (string, string, error) {
	row, err := s.store.TokenByRefresh(ctx, token)
	if err != nil {
		return "", "", op.ErrInvalidRefreshToken
	}
	return row.AccountID, row.ID, nil
}

func (s *storage) SigningKey(context.Context) (op.SigningKey, error) {
	return s.signer, nil
}

func (s *storage) SignatureAlgorithms(context.Context) ([]jose.SignatureAlgorithm, error) {
	return []jose.SignatureAlgorithm{jose.RS256}, nil
}

func (s *storage) KeySet(context.Context) ([]op.Key, error) {
	return []op.Key{&publicKey{id: s.signer.id, key: &s.signer.key.PublicKey}}, nil
}

func (s *storage) GetClientByClientID(ctx context.Context, clientID string) (op.Client, error) {
	if clientID != s.client.id {
		return nil, errs.NotFoundf("No client with ID %s.", clientID)
	}
	return s.client, nil
}

// AuthorizeClientIDSecret always fails: the only client is public, and a public client that
// presents a secret is misconfigured somewhere worth hearing about.
func (s *storage) AuthorizeClientIDSecret(ctx context.Context, clientID, secret string) error {
	return errs.Unauthenticatedf("This provider has no confidential clients.")
}

// SetUserinfoFromScopes fills the userinfo response — and, through the claims assertion, the ID
// token — from the account row. Each scope unlocks its claims and nothing else's.
func (s *storage) SetUserinfoFromScopes(
	ctx context.Context, userinfo *oidc.UserInfo, userID, clientID string, scopes []string,
) error {
	account, err := s.store.AccountByID(ctx, userID)
	if err != nil {
		return err
	}

	userinfo.Subject = account.ID
	for _, scope := range scopes {
		switch scope {
		case oidc.ScopeEmail:
			userinfo.Email = account.Email
			userinfo.EmailVerified = oidc.Bool(true)
		case oidc.ScopeProfile:
			userinfo.Name = account.DisplayName
			userinfo.PreferredUsername = account.Email
			userinfo.UpdatedAt = oidc.FromTime(account.UpdatedAt)
		case oidc.ScopePhone:
			userinfo.PhoneNumber = account.Phone
		}
	}
	return nil
}

// SetUserinfoFromToken serves the userinfo endpoint: the access token has already been verified,
// so this resolves what it may see and delegates to the scope logic above.
func (s *storage) SetUserinfoFromToken(
	ctx context.Context, userinfo *oidc.UserInfo, tokenID, subject, origin string,
) error {
	token, err := s.store.TokenByID(ctx, tokenID)
	if err != nil {
		return err
	}
	return s.SetUserinfoFromScopes(ctx, userinfo, token.AccountID, token.ClientID, token.Scopes)
}

func (s *storage) SetIntrospectionFromToken(
	ctx context.Context, introspection *oidc.IntrospectionResponse, tokenID, subject, clientID string,
) error {
	token, err := s.store.TokenByID(ctx, tokenID)
	if err != nil {
		return err
	}

	userinfo := new(oidc.UserInfo)
	if err := s.SetUserinfoFromScopes(
		ctx, userinfo, token.AccountID, token.ClientID, token.Scopes); err != nil {
		return err
	}
	introspection.SetUserInfo(userinfo)
	introspection.Scope = token.Scopes
	introspection.ClientID = token.ClientID
	return nil
}

// GetPrivateClaimsFromScopes adds the non-standard claims to JWT access tokens.
func (s *storage) GetPrivateClaimsFromScopes(
	ctx context.Context, userID, clientID string, scopes []string,
) (map[string]any, error) {
	// Empty: roles are deliberately not token claims — that would be a second source of truth with
	// a ten-minute lag. Kept as a hook because op asks for it either way.
	return map[string]any{}, nil
}

// GetPrivateClaimsFromRequest is the request-aware variant op prefers when the storage offers it.
// It exists so the session id can ride inside the access token: the request knows which session
// issued it, the scopes alone do not.
func (s *storage) GetPrivateClaimsFromRequest(
	ctx context.Context, request op.TokenRequest, restrictedScopes []string,
) (map[string]any, error) {
	scopes := request.GetScopes()
	if len(restrictedScopes) > 0 {
		scopes = restrictedScopes
	}

	claims, err := s.GetPrivateClaimsFromScopes(ctx, request.GetSubject(), "", scopes)
	if err != nil {
		return nil, err
	}

	if sessionID := sessionIDOf(request); sessionID != "" {
		claims[claimSession] = sessionID
	}
	return claims, nil
}

func (s *storage) GetKeyByIDAndClientID(
	ctx context.Context, keyID, clientID string,
) (*jose.JSONWebKey, error) {
	return nil, errs.NotFoundf("This provider has no JWT-profile clients.")
}

func (s *storage) ValidateJWTProfileScopes(
	ctx context.Context, userID string, scopes []string,
) ([]string, error) {
	return nil, errs.Invalidf("This provider does not support the JWT profile grant.")
}

func (s *storage) Health(ctx context.Context) error {
	return s.store.Health(ctx)
}

// insertToken writes one issued-token row, retiring the refresh token it replaces in the same
// transaction.
//
// retire is empty on the first exchange of a session. On a refresh it is the token being spent: a
// refresh token that has been used ceases to exist, so a replay of it fails loudly instead of
// silently minting a second session.
func (s *storage) insertToken(
	ctx context.Context, request op.TokenRequest, refreshToken, retire string,
) (domain.IssuedToken, error) {
	now := time.Now()
	token := domain.IssuedToken{
		ID:           uuid.NewString(),
		SessionID:    sessionIDOf(request),
		AccountID:    request.GetSubject(),
		RefreshToken: refreshToken,
		Scopes:       request.GetScopes(),
		Audience:     request.GetAudience(),
		AuthTime:     now,
		ExpiresAt:    now.Add(s.tokenTTL),
		CreatedAt:    now,
	}

	switch r := request.(type) {
	case *authRequest:
		token.ClientID = r.row.ClientID
		token.AuthTime = r.row.AuthTime
	case *refreshTokenRequest:
		token.ClientID = r.row.ClientID
		token.AuthTime = r.row.AuthTime
	default:
		// Some other grant produced this request. The only client is the desktop one, so
		// attribute it there rather than storing an empty client id.
		token.ClientID = s.client.id
	}

	if token.SessionID == "" {
		return domain.IssuedToken{}, errs.Internalf(
			fmt.Errorf("token request %T carries no session", request),
			"issue token")
	}

	if err := s.store.RotateToken(ctx, retire, token); err != nil {
		return domain.IssuedToken{}, err
	}
	return token, nil
}

// sessionIDOf extracts which session a token request belongs to, for the shapes that know.
func sessionIDOf(request op.TokenRequest) string {
	switch r := request.(type) {
	case *authRequest:
		return r.row.SessionID
	case *refreshTokenRequest:
		return r.row.SessionID
	default:
		return ""
	}
}
