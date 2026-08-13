package store

import (
	"context"
	"database/sql"
	"errors"
	"strings"
	"time"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

const accountColumns = `a.Id, a.Email, a.DisplayName, a.Phone, a.PasswordHash, a.TeamId,
            a.LastSignInAt, a.IsActive, a.CreatedAt, a.UpdatedAt`

// AccountByID returns the account, or an errs.NotFound error.
func (s *SQLServer) AccountByID(ctx context.Context, id string) (domain.Account, error) {
	const q = `
        SELECT ` + accountColumns + `
        FROM account.Account AS a
        WHERE a.Id = @p1;`
	return s.scanAccount(s.db.QueryRowContext(ctx, q, id), "id "+id)
}

// AccountByEmail returns the account for an address, or an errs.NotFound error.
func (s *SQLServer) AccountByEmail(ctx context.Context, email string) (domain.Account, error) {
	const q = `
        SELECT ` + accountColumns + `
        FROM account.Account AS a
        WHERE a.Email = @p1;`
	return s.scanAccount(s.db.QueryRowContext(ctx, q, strings.ToLower(email)), "email "+email)
}

// InsertAccount stores a new account. A duplicate address is a Conflict, not an Internal error.
func (s *SQLServer) InsertAccount(ctx context.Context, u domain.Account) error {
	const q = `
        INSERT INTO account.Account
            (Id, Email, DisplayName, Phone, PasswordHash, TeamId, IsActive, CreatedAt, UpdatedAt)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9);`

	_, err := s.db.ExecContext(ctx, q,
		u.ID, u.Email, u.DisplayName, u.Phone, u.PasswordHash, nullable(u.TeamID), u.IsActive,
		storable(u.CreatedAt), storable(u.UpdatedAt))
	if err != nil {
		if isUniqueViolation(err) {
			return errs.Conflictf("An account with that email address already exists.")
		}
		return errs.Internalf(err, "insert account")
	}
	return nil
}

// UpdateAccount rewrites the parts of an account its owner edits.
//
// LastSignInAt and IsActive are deliberately NOT in the SET list: both are written by other code
// paths — TouchSignIn stamps one, SetActive flips the other — and a profile save that included
// them would write back whatever this copy was loaded with, silently re-enabling an account an
// administrator just disabled. Each column belongs to exactly one statement.
func (s *SQLServer) UpdateAccount(ctx context.Context, u domain.Account) error {
	const q = `
        UPDATE account.Account
        SET DisplayName = @p1, Phone = @p2, PasswordHash = @p3, TeamId = @p4, UpdatedAt = @p5
        WHERE Id = @p6;`

	res, err := s.db.ExecContext(ctx, q,
		u.DisplayName, u.Phone, u.PasswordHash, nullable(u.TeamID),
		storable(u.UpdatedAt), u.ID)
	if err != nil {
		return errs.Internalf(err, "update account")
	}
	return requireOneRow(res, "No account with ID %s.", u.ID)
}

// visibleAccounts turns the caller's row scope into a predicate.
//
// Written as a separate WHERE per scope rather than one clause with ORs, because an OR chain over
// three mutually exclusive cases is not sargable: SQL Server cannot use the index on TeamId when
// the predicate also has to consider that the scope might have been "all".
func (s *SQLServer) visibleAccounts(ctx context.Context) (string, []any) {
	subject, signedIn := auth.SubjectFrom(ctx)
	if !signedIn {
		return "WHERE 1 = 0", nil
	}

	switch auth.ScopeOf(ctx, accountapi.PermissionManageUsers) {
	case auth.ScopeAll:
		return "", nil

	case auth.ScopeTeam:
		// An account with no team is on nobody's team, including its own reading of this: the
		// IS NOT NULL keeps a null TeamId from matching every other null one.
		return `WHERE a.TeamId IS NOT NULL
                  AND a.TeamId = (
                      SELECT c.TeamId FROM account.Account AS c WHERE c.Id = @p1
                  )`, []any{subject.ID}

	case auth.ScopeOwn:
		return "WHERE a.Id = @p1", []any{subject.ID}

	default:
		return "WHERE 1 = 0", nil
	}
}

func (s *SQLServer) scanAccount(row scanner, what string) (domain.Account, error) {
	var (
		u          domain.Account
		teamID     sql.NullString
		lastSignIn sql.NullTime
	)

	err := row.Scan(&u.ID, &u.Email, &u.DisplayName, &u.Phone, &u.PasswordHash, &teamID,
		&lastSignIn, &u.IsActive, &u.CreatedAt, &u.UpdatedAt)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Account{}, errs.NotFoundf("No account for %s.", what)
	}
	if err != nil {
		return domain.Account{}, errs.Internalf(err, "scan account")
	}

	u.TeamID = teamID.String
	u.LastSignInAt = lastSignIn.Time.UTC()
	u.CreatedAt = u.CreatedAt.UTC()
	u.UpdatedAt = u.UpdatedAt.UTC()
	return u, nil
}

// Accounts lists the accounts the CALLER may see, newest first — not always all of them.
//
// Unpaged: it serves an administration screen whose design is a filterable table over the whole
// set. A deployment large enough for that to hurt needs paging in the contract, not a TOP the
// client cannot see.
//
// A grant of users.manage carries own, team or all, and the narrowing belongs here rather than in
// the route gate: a gate that rewrote everyone's SQL would have to understand everyone's schema,
// while a store narrowing its own query only has to understand its own. TeamId is what "team"
// means, and this narrowing is its only reader.
//
// An unrecognised or absent scope narrows to nothing rather than widening to everything. The route
// gate has already established that the caller holds the permission; if this cannot tell how far it
// reaches, the safe answer is the smaller one.
func (s *SQLServer) Accounts(ctx context.Context) ([]domain.Account, error) {
	where, args := s.visibleAccounts(ctx)

	q := `
        SELECT ` + accountColumns + `
        FROM account.Account AS a
        ` + where + `
        ORDER BY a.CreatedAt DESC, a.Id DESC;`

	rows, err := s.db.QueryContext(ctx, q, args...)
	if err != nil {
		return nil, errs.Internalf(err, "list accounts")
	}
	defer func() { _ = rows.Close() }()

	out := []domain.Account{}
	for rows.Next() {
		u, err := s.scanAccount(rows, "a row")
		if err != nil {
			return nil, err
		}
		out = append(out, u)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list accounts")
	}
	return out, nil
}

// TouchSignIn records that the account just signed in.
//
// One narrow UPDATE rather than a read-modify-write through UpdateAccount: a sign-in landing at
// the same moment as a profile edit must not have either overwrite the other's columns.
func (s *SQLServer) TouchSignIn(ctx context.Context, id string, at time.Time) error {
	const q = `UPDATE account.Account SET LastSignInAt = @p1 WHERE Id = @p2;`

	if _, err := s.db.ExecContext(ctx, q, at.UTC(), id); err != nil {
		return errs.Internalf(err, "record sign-in")
	}
	return nil
}

// SetActive switches an account on or off.
func (s *SQLServer) SetActive(ctx context.Context, id string, active bool) error {
	const q = `UPDATE account.Account SET IsActive = @p1 WHERE Id = @p2;`

	res, err := s.db.ExecContext(ctx, q, active, id)
	if err != nil {
		return errs.Internalf(err, "set account status")
	}
	return requireOneRow(res, "No account with ID %s.", id)
}

// DeleteAccount removes the account row and its security events, in one transaction.
//
// Sessions and issued tokens go by cascade; SecurityEvent has no foreign key — the trail outlives
// sessions on purpose — so it is swept here explicitly. Half a deletion is the one outcome worse
// than either whole one, hence the transaction.
func (s *SQLServer) DeleteAccount(ctx context.Context, id string) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return errs.Internalf(err, "delete account")
	}
	defer func() { _ = tx.Rollback() }()

	if _, err := tx.ExecContext(
		ctx, `DELETE FROM account.SecurityEvent WHERE AccountId = @p1;`, id); err != nil {
		return errs.Internalf(err, "delete account events")
	}

	// The authorization module's rows go in this transaction, not via a published event: an event
	// delivered after the commit can fail while the delete stands, leaving a membership that
	// over-counts "how many people hold this role" forever. One transaction is the only shape
	// where the account and its memberships go together.
	//
	// A cross-schema DELETE rather than a foreign key because authz.AccountRole must not depend on
	// account.Account existing — the authorization module is meant to survive the account module
	// being swapped for somebody else's identity provider.
	if _, err := tx.ExecContext(
		ctx, `DELETE FROM authz.AccountRole WHERE AccountId = @p1;`, id); err != nil {
		return errs.Internalf(err, "delete account roles")
	}

	res, err := tx.ExecContext(ctx, `DELETE FROM account.Account WHERE Id = @p1;`, id)
	if err != nil {
		return errs.Internalf(err, "delete account")
	}
	if err := requireOneRow(res, "No account with ID %s.", id); err != nil {
		return err
	}
	if err := tx.Commit(); err != nil {
		return errs.Internalf(err, "delete account")
	}
	return nil
}

// nullableTime maps the zero time onto NULL, which is how "never signed in" is stored.
func nullableTime(t time.Time) any {
	if t.IsZero() {
		return nil
	}
	return t.UTC()
}
