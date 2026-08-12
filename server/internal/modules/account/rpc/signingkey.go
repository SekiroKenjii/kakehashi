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

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/store"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

type signingKey struct {
	id  string
	key *rsa.PrivateKey
}

func (s *signingKey) SignatureAlgorithm() jose.SignatureAlgorithm { return jose.RS256 }
func (s *signingKey) Key() any                                    { return s.key }
func (s *signingKey) ID() string                                  { return s.id }

var _ op.SigningKey = (*signingKey)(nil)

// The same key's public half, as the JWKS endpoint publishes it.
type publicKey struct {
	id  string
	key *rsa.PublicKey
}

func (p *publicKey) ID() string                         { return p.id }
func (p *publicKey) Algorithm() jose.SignatureAlgorithm { return jose.RS256 }
func (p *publicKey) Use() string                        { return "sig" }
func (p *publicKey) Key() any                           { return p.key }

var _ op.Key = (*publicKey)(nil)

// The key goes to the database rather than a file so every replica signs with the same key, and so
// a redeploy does not invalidate every token in the field.
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
