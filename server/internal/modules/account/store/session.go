package store

import (
	"context"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

func (s *SQLServer) InsertSession(ctx context.Context, sess domain.UserSession) error {
	const q = `
        INSERT INTO account.UserSession
            (Id, AccountId, ClientId, Device, IpAddress, CreatedAt, LastSeenAt)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7);`

	_, err := s.db.ExecContext(ctx, q, sess.ID, sess.UserID, sess.ClientID, sess.Device,
		sess.IPAddress, storable(sess.CreatedAt), storable(sess.LastSeenAt))
	if err != nil {
		return errs.Internalf(err, "insert session")
	}
	return nil
}

func (s *SQLServer) SessionsForUser(
	ctx context.Context, accountID string,
) ([]domain.UserSession, error) {
	const q = `
        SELECT us.Id, us.AccountId, us.ClientId, us.Device, us.IpAddress, us.CreatedAt,
            us.LastSeenAt
        FROM account.UserSession AS us
        WHERE us.AccountId = @p1
        ORDER BY us.LastSeenAt DESC, us.Id DESC;`

	rows, err := s.db.QueryContext(ctx, q, accountID)
	if err != nil {
		return nil, errs.Internalf(err, "list sessions")
	}
	defer rows.Close()

	var sessions []domain.UserSession
	for rows.Next() {
		var sess domain.UserSession
		if err := rows.Scan(&sess.ID, &sess.UserID, &sess.ClientID, &sess.Device,
			&sess.IPAddress, &sess.CreatedAt, &sess.LastSeenAt); err != nil {
			return nil, errs.Internalf(err, "scan session")
		}
		sess.CreatedAt = sess.CreatedAt.UTC()
		sess.LastSeenAt = sess.LastSeenAt.UTC()
		sessions = append(sessions, sess)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list sessions")
	}
	return sessions, nil
}

// Keeps "last seen" honest across silent token refreshes.
func (s *SQLServer) TouchSession(ctx context.Context, id string, at time.Time) error {
	const q = `
        UPDATE account.UserSession
        SET LastSeenAt = @p1
        WHERE Id = @p2;`

	if _, err := s.db.ExecContext(ctx, q, storable(at), id); err != nil {
		return errs.Internalf(err, "touch session")
	}
	return nil
}

// The account id is part of the predicate so a stolen session id cannot be used to end someone
// else's.
//
// Whether a row went is returned rather than swallowed because a caller announces this delete, and
// an announcement about a row that was not there is a false entry in somebody's security feed.
func (s *SQLServer) DeleteSession(ctx context.Context, accountID, id string) (bool, error) {
	const q = `DELETE FROM account.UserSession WHERE AccountId = @p1 AND Id = @p2;`

	res, err := s.db.ExecContext(ctx, q, accountID, id)
	if err != nil {
		return false, errs.Internalf(err, "delete session")
	}

	affected, err := res.RowsAffected()
	if err != nil {
		return false, errs.Internalf(err, "delete session")
	}
	return affected > 0, nil
}

func (s *SQLServer) DeleteSessionsForUser(ctx context.Context, accountID string) (int64, error) {
	const q = `DELETE FROM account.UserSession WHERE AccountId = @p1;`

	res, err := s.db.ExecContext(ctx, q, accountID)
	if err != nil {
		return 0, errs.Internalf(err, "delete sessions")
	}

	affected, err := res.RowsAffected()
	if err != nil {
		return 0, errs.Internalf(err, "delete sessions")
	}
	return affected, nil
}

func (s *SQLServer) DeleteSessionsForUserClient(
	ctx context.Context, accountID, clientID string,
) error {
	const q = `DELETE FROM account.UserSession WHERE AccountId = @p1 AND ClientId = @p2;`

	if _, err := s.db.ExecContext(ctx, q, accountID, clientID); err != nil {
		return errs.Internalf(err, "delete sessions for client")
	}
	return nil
}

// One query for the whole list screen rather than one per row. Accounts with no open session are
// absent from the map rather than present as zero, because that is what a GROUP BY returns and
// inventing the missing keys here would mean reading the account table from the session store.
func (s *SQLServer) SessionCountsByAccount(ctx context.Context) (map[string]int, error) {
	const q = `
        SELECT us.AccountId, COUNT(*)
        FROM account.UserSession AS us
        GROUP BY us.AccountId;`

	rows, err := s.db.QueryContext(ctx, q)
	if err != nil {
		return nil, errs.Internalf(err, "count sessions")
	}
	defer func() { _ = rows.Close() }()

	out := map[string]int{}
	for rows.Next() {
		var (
			id    string
			count int
		)
		if err := rows.Scan(&id, &count); err != nil {
			return nil, errs.Internalf(err, "scan session count")
		}
		out[id] = count
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "count sessions")
	}
	return out, nil
}
