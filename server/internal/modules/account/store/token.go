package store

import (
	"context"
	"database/sql"
	"errors"
	"strings"

	"__GO_MODULE__/server/internal/modules/account/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

// InsertToken records an issued token.
func (s *SQLServer) InsertToken(ctx context.Context, t domain.IssuedToken) error {
	return s.RotateToken(ctx, "", t)
}

// RotateToken retires a refresh token and issues its replacement, as one act.
//
// One transaction, because a gap between the DELETE and the INSERT can destroy the user's only
// refresh token — the credential they cannot re-obtain without signing in again. Rotation that can
// lose the thing it is rotating is worse than no rotation.
//
// An empty retire means "this is the first token of a session", so one method serves both and there
// is no second insert path to keep in step.
func (s *SQLServer) RotateToken(ctx context.Context, retire string, t domain.IssuedToken) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return errs.Internalf(err, "rotate token")
	}
	defer func() { _ = tx.Rollback() }()

	if retire != "" {
		const del = `DELETE FROM account.IssuedToken WHERE RefreshToken = @p1;`
		if _, err := tx.ExecContext(ctx, del, retire); err != nil {
			return errs.Internalf(err, "delete refresh token")
		}
	}

	const q = `
        INSERT INTO account.IssuedToken
            (Id, SessionId, AccountId, ClientId, RefreshToken, Scopes, Audience, AuthTime,
             ExpiresAt, CreatedAt)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10);`

	_, err = tx.ExecContext(ctx, q, t.ID, t.SessionID, t.AccountID, t.ClientID,
		nullable(t.RefreshToken), strings.Join(t.Scopes, " "), strings.Join(t.Audience, " "),
		storable(t.AuthTime), storable(t.ExpiresAt), storable(t.CreatedAt))
	if err != nil {
		return errs.Internalf(err, "insert token")
	}

	if err := tx.Commit(); err != nil {
		return errs.Internalf(err, "rotate token")
	}
	return nil
}

// TokenByRefresh returns the token record a refresh token belongs to.
func (s *SQLServer) TokenByRefresh(ctx context.Context, refreshToken string) (domain.IssuedToken, error) {
	const q = `
        SELECT t.Id, t.SessionId, t.AccountId, t.ClientId, t.RefreshToken, t.Scopes, t.Audience,
            t.AuthTime, t.ExpiresAt, t.CreatedAt
        FROM account.IssuedToken AS t
        WHERE t.RefreshToken = @p1;`

	var (
		t        domain.IssuedToken
		refresh  sql.NullString
		scopes   string
		audience string
	)

	err := s.db.QueryRowContext(ctx, q, refreshToken).Scan(&t.ID, &t.SessionID, &t.AccountID,
		&t.ClientID, &refresh, &scopes, &audience, &t.AuthTime, &t.ExpiresAt, &t.CreatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.IssuedToken{}, errs.Unauthenticatedf("That refresh token is not valid.")
	}
	if err != nil {
		return domain.IssuedToken{}, errs.Internalf(err, "read refresh token")
	}

	t.RefreshToken = refresh.String
	t.Scopes = strings.Fields(scopes)
	t.Audience = strings.Fields(audience)
	t.AuthTime = t.AuthTime.UTC()
	t.ExpiresAt = t.ExpiresAt.UTC()
	t.CreatedAt = t.CreatedAt.UTC()
	return t, nil
}

// TokenByID returns one issued token, or an errs.NotFound error. The userinfo endpoint uses it to
// learn which scopes a verified access token was issued with.
func (s *SQLServer) TokenByID(ctx context.Context, id string) (domain.IssuedToken, error) {
	const q = `
        SELECT t.Id, t.SessionId, t.AccountId, t.ClientId, t.RefreshToken, t.Scopes, t.Audience,
            t.AuthTime, t.ExpiresAt, t.CreatedAt
        FROM account.IssuedToken AS t
        WHERE t.Id = @p1;`

	var (
		t        domain.IssuedToken
		refresh  sql.NullString
		scopes   string
		audience string
	)

	err := s.db.QueryRowContext(ctx, q, id).Scan(&t.ID, &t.SessionID, &t.AccountID,
		&t.ClientID, &refresh, &scopes, &audience, &t.AuthTime, &t.ExpiresAt, &t.CreatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.IssuedToken{}, errs.NotFoundf("No token with ID %s.", id)
	}
	if err != nil {
		return domain.IssuedToken{}, errs.Internalf(err, "read token")
	}

	t.RefreshToken = refresh.String
	t.Scopes = strings.Fields(scopes)
	t.Audience = strings.Fields(audience)
	t.AuthTime = t.AuthTime.UTC()
	t.ExpiresAt = t.ExpiresAt.UTC()
	t.CreatedAt = t.CreatedAt.UTC()
	return t, nil
}

// DeleteToken removes one issued token by its id.
func (s *SQLServer) DeleteToken(ctx context.Context, id string) error {
	const q = `DELETE FROM account.IssuedToken WHERE Id = @p1;`

	if _, err := s.db.ExecContext(ctx, q, id); err != nil {
		return errs.Internalf(err, "delete token")
	}
	return nil
}

// DeleteTokenByRefresh removes the token a refresh token belongs to. Used on rotation, so the
// previous refresh token cannot be replayed.
func (s *SQLServer) DeleteTokenByRefresh(ctx context.Context, refreshToken string) error {
	const q = `DELETE FROM account.IssuedToken WHERE RefreshToken = @p1;`

	if _, err := s.db.ExecContext(ctx, q, refreshToken); err != nil {
		return errs.Internalf(err, "delete refresh token")
	}
	return nil
}
