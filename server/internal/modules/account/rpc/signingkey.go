package rpc

import (
	"context"
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"encoding/pem"
	"fmt"
	"time"

	jose "github.com/go-jose/go-jose/v4"
	"github.com/google/uuid"
	"github.com/zitadel/oidc/v3/pkg/op"

	"__GO_MODULE__/server/internal/modules/account/domain"
	"__GO_MODULE__/server/internal/modules/account/store"
	"__GO_MODULE__/server/internal/platform/errs"
)

// The provider's token-signing key: minted on first boot, parsed on every boot after, and
// presented in the two shapes op asks for.

// signingKey is the provider's private key, loaded from the store.
type signingKey struct {
	id  string
	key *rsa.PrivateKey
}

func (s *signingKey) SignatureAlgorithm() jose.SignatureAlgorithm { return jose.RS256 }
func (s *signingKey) Key() any                                    { return s.key }
func (s *signingKey) ID() string                                  { return s.id }

var _ op.SigningKey = (*signingKey)(nil)

// publicKey is the same key's public half, as the JWKS endpoint publishes it.
type publicKey struct {
	id  string
	key *rsa.PublicKey
}

func (p *publicKey) ID() string                         { return p.id }
func (p *publicKey) Algorithm() jose.SignatureAlgorithm { return jose.RS256 }
func (p *publicKey) Use() string                        { return "sig" }
func (p *publicKey) Key() any                           { return p.key }

var _ op.Key = (*publicKey)(nil)

// loadOrCreateSigningKey returns the provider's key, generating and persisting one on first boot.
//
// The key lives in the database rather than a file so that every replica of the server signs with
// the same key, and so a redeploy does not invalidate every token in the field. Rotation is a
// matter of inserting a newer row: SigningKey reads the latest, KeySet could serve the history.
func loadOrCreateSigningKey(ctx context.Context, st *store.SQLServer) (*signingKey, error) {
	existing, err := st.SigningKey(ctx)
	if err == nil {
		return parseSigningKey(existing)
	}
	if errs.KindOf(err) != errs.NotFound {
		return nil, err
	}

	generated, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		return nil, errs.Internalf(err, "generate signing key")
	}

	der, err := x509.MarshalPKCS8PrivateKey(generated)
	if err != nil {
		return nil, errs.Internalf(err, "encode signing key")
	}

	row := domain.SigningKey{
		ID:        uuid.NewString(),
		Algorithm: string(jose.RS256),
		PrivateKey: string(pem.EncodeToMemory(
			&pem.Block{Type: "PRIVATE KEY", Bytes: der})),
		CreatedAt: time.Now(),
	}
	if err := st.InsertSigningKey(ctx, row); err != nil {
		return nil, err
	}

	return &signingKey{id: row.ID, key: generated}, nil
}

func parseSigningKey(row domain.SigningKey) (*signingKey, error) {
	block, _ := pem.Decode([]byte(row.PrivateKey))
	if block == nil {
		return nil, errs.Internalf(nil, "signing key %s is not PEM", row.ID)
	}

	parsed, err := x509.ParsePKCS8PrivateKey(block.Bytes)
	if err != nil {
		return nil, errs.Internalf(err, "parse signing key %s", row.ID)
	}

	rsaKey, ok := parsed.(*rsa.PrivateKey)
	if !ok {
		return nil, errs.Internalf(
			fmt.Errorf("key is %T", parsed), "signing key %s is not RSA", row.ID)
	}

	return &signingKey{id: row.ID, key: rsaKey}, nil
}

var _ op.Storage = (*storage)(nil)
var _ op.CanGetPrivateClaimsFromRequest = (*storage)(nil)
