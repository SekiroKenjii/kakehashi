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

// verifier turns a bearer token into a platform Subject. It is the account module's
// implementation of auth.Verifier, published on the kernel so the mux — which may not import this
// module — can pick it up from the registry.
//
// Verification is entirely local: signature against the provider's own key, issuer, expiry. No
// database is consulted, which is what keeps it off the hot path's critical resources — and is
// also its known trade-off: a revoked session's access token stays valid until it expires. That is
// why the access-token lifetime is minutes, not hours; revocation takes effect at the next
// refresh, where the database is consulted.
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

	// sid is required: an access token from this provider always carries it and an ID token never
	// does, and tokens carrying ID-token markers are rejected below.
	// docs/adr/0006-id-token-is-not-an-access-token.md
	sid, _ := claims.Claims[claimSession].(string)
	if sid == "" {
		return auth.Subject{}, errs.Unauthenticatedf(
			"That is not an access token. Send the access token rather than the ID token.")
	}

	// The positive half of the same check: these three claims are ID-token markers, and none of
	// them belongs on an access token.
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

// staticKeySet verifies signatures against the provider's one signing key.
//
// oidc.KeySet is usually a remote JWKS that refreshes itself; this process *is* the issuer, so
// the key is simply in hand. When rotation arrives, this type grows a list.
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
