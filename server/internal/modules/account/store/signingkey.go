package store

import (
	"context"
	"database/sql"
	"errors"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

func (s *SQLServer) SigningKey(ctx context.Context) (domain.SigningKey, error) {
	const q = `
        SELECT TOP (1) sk.Id, sk.Algorithm, sk.PrivateKey, sk.CreatedAt
        FROM account.SigningKey AS sk
        ORDER BY sk.CreatedAt DESC;`

	var k domain.SigningKey
	err := s.db.QueryRowContext(ctx, q).Scan(&k.ID, &k.Algorithm, &k.PrivateKey, &k.CreatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.SigningKey{}, errs.NotFoundf("No signing key has been created yet.")
	}
	if err != nil {
		return domain.SigningKey{}, errs.Internalf(err, "read signing key")
	}

	k.CreatedAt = k.CreatedAt.UTC()
	return k, nil
}

func (s *SQLServer) InsertSigningKey(ctx context.Context, k domain.SigningKey) error {
	const q = `
        INSERT INTO account.SigningKey (Id, Algorithm, PrivateKey, CreatedAt)
        VALUES (@p1, @p2, @p3, @p4);`

	_, err := s.db.ExecContext(ctx, q, k.ID, k.Algorithm, k.PrivateKey, storable(k.CreatedAt))
	if err != nil {
		return errs.Internalf(err, "insert signing key")
	}
	return nil
}
