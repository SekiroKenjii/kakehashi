package store

import (
	"context"
	"database/sql"
	"errors"
	"strings"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

const authRequestColumns = `r.Id, r.ClientId, r.Subject, r.Scopes, r.RedirectUri,
            r.ResponseType, r.Nonce, r.AuthState, r.CodeChallenge, r.CodeChallengeMethod,
            r.AuthCode, r.SessionId, r.IsDone, r.AuthTime, r.CreatedAt`

func (s *SQLServer) InsertAuthRequest(ctx context.Context, r domain.AuthRequest) error {
	const q = `
        INSERT INTO account.AuthRequest
            (Id, ClientId, Subject, Scopes, RedirectUri, ResponseType, Nonce, AuthState,
             CodeChallenge, CodeChallengeMethod, IsDone, CreatedAt)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, 0, @p11);`

	_, err := s.db.ExecContext(ctx, q,
		r.ID, r.ClientID, r.Subject, strings.Join(r.Scopes, " "), r.RedirectURI, r.ResponseType,
		r.Nonce, r.State, r.CodeChallenge, r.CodeChallengeMethod, storable(r.CreatedAt))
	if err != nil {
		return errs.Internalf(err, "insert auth request")
	}
	return nil
}

func (s *SQLServer) AuthRequestByID(ctx context.Context, id string) (domain.AuthRequest, error) {
	const q = `
        SELECT ` + authRequestColumns + `
        FROM account.AuthRequest AS r
        WHERE r.Id = @p1;`
	return s.scanAuthRequest(s.db.QueryRowContext(ctx, q, id))
}

func (s *SQLServer) AuthRequestByCode(ctx context.Context, code string) (domain.AuthRequest, error) {
	const q = `
        SELECT ` + authRequestColumns + `
        FROM account.AuthRequest AS r
        WHERE r.AuthCode = @p1;`
	return s.scanAuthRequest(s.db.QueryRowContext(ctx, q, code))
}

func (s *SQLServer) SaveAuthCode(ctx context.Context, id, code string) error {
	const q = `
        UPDATE account.AuthRequest
        SET AuthCode = @p1
        WHERE Id = @p2;`

	res, err := s.db.ExecContext(ctx, q, code, id)
	if err != nil {
		return errs.Internalf(err, "save auth code")
	}
	return requireOneRow(res, "No authorization request with ID %s.", id)
}

// The session id is recorded so the token exchange can attach its tokens to the session the
// sign-in created.
func (s *SQLServer) CompleteAuthRequest(
	ctx context.Context, id, subject, sessionID string, at time.Time,
) error {
	const q = `
        UPDATE account.AuthRequest
        SET Subject = @p1, SessionId = @p2, IsDone = 1, AuthTime = @p3
        WHERE Id = @p4;`

	res, err := s.db.ExecContext(ctx, q, subject, sessionID, storable(at), id)
	if err != nil {
		return errs.Internalf(err, "complete auth request")
	}
	return requireOneRow(res, "No authorization request with ID %s.", id)
}

func (s *SQLServer) DeleteAuthRequest(ctx context.Context, id string) error {
	const q = `DELETE FROM account.AuthRequest WHERE Id = @p1;`

	if _, err := s.db.ExecContext(ctx, q, id); err != nil {
		return errs.Internalf(err, "delete auth request")
	}
	return nil
}

func (s *SQLServer) scanAuthRequest(row scanner) (domain.AuthRequest, error) {
	var (
		r        domain.AuthRequest
		scopes   string
		code     sql.NullString
		authTime sql.NullTime
	)

	err := row.Scan(&r.ID, &r.ClientID, &r.Subject, &scopes, &r.RedirectURI, &r.ResponseType,
		&r.Nonce, &r.State, &r.CodeChallenge, &r.CodeChallengeMethod, &code, &r.SessionID,
		&r.Done, &authTime, &r.CreatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.AuthRequest{}, errs.NotFoundf("That sign-in request has expired.")
	}
	if err != nil {
		return domain.AuthRequest{}, errs.Internalf(err, "scan auth request")
	}

	r.Scopes = strings.Fields(scopes)
	r.Code = code.String
	r.AuthTime = authTime.Time.UTC()
	r.CreatedAt = r.CreatedAt.UTC()
	return r, nil
}
