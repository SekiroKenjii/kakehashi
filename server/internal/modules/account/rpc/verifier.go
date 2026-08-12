package rpc

import (
	"context"
	"fmt"

	jose "github.com/go-jose/go-jose/v4"
	"github.com/zitadel/oidc/v3/pkg/oidc"
	"github.com/zitadel/oidc/v3/pkg/op"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Verification is entirely local: signature against the provider's own key, issuer, expiry. No
// database is consulted, which keeps it off the hot path — and is also its known trade-off: a
// revoked session's access token stays valid until it expires. That is why the access-token
// lifetime is minutes, not hours; revocation takes effect at the next refresh, where the database
// is consulted.
type verifier struct {
	inner *op.AccessTokenVerifier
}

func newVerifier(issuer string, key *publicKey) *verifier {
	return &verifier{
		inner: op.NewAccessTokenVerifier(issuer, &staticKeySet{key: key}),
	}
}

func (v *verifier) Verify(ctx context.Context, token string) (auth.Subject, error) {
	claims, err := op.VerifyAccessToken[*oidc.AccessTokenClaims](ctx, token, v.inner)
	if err != nil {
		return auth.Subject{}, errs.Unauthenticatedf("That token is not valid.")
	}

	// VerifyAccessToken checks the issuer, the signature and the expiry, and nothing about what KIND
	// of token this is. That is not enough here, because this process issues two kinds: sign-in
	// hands the client an ID token beside the access token, and an ID token from this issuer passes
	// every check above. It authenticated every endpoint for its full hour and — because nothing
	// about it is a session — it kept working after sign-out, after the session was revoked, and
	// after the account was deactivated. A credential that outlives the act of revoking it is
	// worse than a long-lived one.
	//
	// The session id is what tells them apart, and not something more obvious because both tokens
	// carry client_id and the same aud. sid is set by GetPrivateClaimsFromRequest, which runs only
	// for an access token, and sessionIDOf covers both grants that produce one — the authorization
	// code and the refresh. An access token from this provider always has it; an ID token never
	// does.
	//
	// It is also the claim that makes the token revocable at all: the session it names is what
	// sign-out deletes.
	sid, _ := claims.Claims[claimSession].(string)
	if sid == "" {
		return auth.Subject{}, errs.Unauthenticatedf(
			"That is not an access token. Send the access token rather than the ID token.")
	}

	// The positive half of the same question, in case a future grant type mints an access token by
	// some path that skips the private claims. These three are the OpenID Connect ID token's own
	// signature — the hash of the access token it was issued beside, the party it was issued to,
	// and when the user actually authenticated — and none of them belongs on an access token.
	for _, marker := range []string{"at_hash", "azp", "auth_time"} {
		if _, present := claims.Claims[marker]; present {
			return auth.Subject{}, errs.Unauthenticatedf(
				"That is not an access token. Send the access token rather than the ID token.")
		}
	}

	// Roles are deliberately not read from here — authorization is resolved per request, not from a
	// token that lives ten minutes.
	return auth.Subject{ID: claims.Subject, SessionID: sid}, nil
}

var _ auth.Verifier = (*verifier)(nil)

// oidc.KeySet is usually a remote JWKS that refreshes itself; this process is the issuer, so the
// key is simply in hand. When rotation arrives, this type grows a list.
type staticKeySet struct {
	key *publicKey
}

func (s *staticKeySet) VerifySignature(
	ctx context.Context, jws *jose.JSONWebSignature,
) ([]byte, error) {
	for _, signature := range jws.Signatures {
		if signature.Header.KeyID != "" && signature.Header.KeyID != s.key.id {
			continue
		}
		return jws.Verify(s.key.key)
	}
	return nil, fmt.Errorf("no signature matches key %s", s.key.id)
}
