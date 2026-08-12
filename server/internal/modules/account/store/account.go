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

func (s *SQLServer) AccountByID(ctx context.Context, id string) (domain.Account, error) {
	const q = `
        SELECT ` + accountColumns + `
        FROM account.Account AS a
        WHERE a.Id = @p1;`
	return s.scanAccount(s.db.QueryRowContext(ctx, q, id), "id "+id)
}

func (s *SQLServer) AccountByEmail(ctx context.Context, email string) (domain.Account, error) {
	const q = `
        SELECT ` + accountColumns + `
        FROM account.Account AS a
        WHERE a.Email = @p1;`
	return s.scanAccount(s.db.QueryRowContext(ctx, q, strings.ToLower(email)), "email "+email)
}

// A duplicate address is a Conflict, not an Internal error.
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

// LastSignInAt and IsActive are deliberately not in the SET list. Both are written by other code
// paths — signing in stamps one, an administrator deactivating an account flips the other — so
// including them here meant an ordinary profile save wrote back whatever this copy happened to be
// loaded with: somebody editing their display name while an administrator disabled them silently
// switched themselves back on.
//
// Each column belongs to exactly one statement: TouchSignIn owns the stamp, SetActive owns the
// flag.
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

// A separate WHERE per scope rather than one clause with ORs, because an OR chain over three
// mutually exclusive cases is not sargable: SQL Server cannot use the index on TeamId when the
// predicate also has to consider that the scope might have been "all".
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

// Lists the accounts the CALLER may see, newest first, which is not always all of them.
//
// Unpaged, a known limit: it serves an administration screen whose own design is a filterable table
// over the whole set. A deployment large enough for that to hurt needs paging in the contract, not
// a TOP the client cannot see.
//
// A grant of users.manage carries own, team or all, and the narrowing belongs here rather than in
// the route gate for the reason the platform's ScopeOf comment gives: a gate that rewrote
// everyone's SQL would have to understand everyone's schema, while a store narrowing its own query
// only has to understand its own. This one understands that TeamId is what "team" means.
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

// One narrow UPDATE rather than a read-modify-write through UpdateAccount: a sign-in landing at
// the same moment as a profile edit must not have either overwrite the other's columns.
func (s *SQLServer) TouchSignIn(ctx context.Context, id string, at time.Time) error {
	const q = `UPDATE account.Account SET LastSignInAt = @p1 WHERE Id = @p2;`

	if _, err := s.db.ExecContext(ctx, q, at.UTC(), id); err != nil {
		return errs.Internalf(err, "record sign-in")
	}
	return nil
}

func (s *SQLServer) SetActive(ctx context.Context, id string, active bool) error {
	const q = `UPDATE account.Account SET IsActive = @p1 WHERE Id = @p2;`

	res, err := s.db.ExecContext(ctx, q, active, id)
	if err != nil {
		return errs.Internalf(err, "set account status")
	}
	return requireOneRow(res, "No account with ID %s.", id)
}

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

	// The authorization module's rows, deleted here rather than by publishing an event for it to
	// react to — a deliberate exception to how the two modules otherwise talk. A role membership
	// pointing at an account that no longer exists is over-reported in every "how many people hold
	// this role" count, forever, and an event delivered after the commit can fail while the delete
	// stands.
	//
	// It is a cross-schema DELETE rather than a foreign key because authz.AccountRole must not
	// depend on account.Account existing — the authorization module is meant to survive the account
	// module being swapped for somebody else's identity provider.
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

// The zero time maps onto NULL, which is how "never signed in" is stored.
func nullableTime(t time.Time) any {
	if t.IsZero() {
		return nil
	}
	return t.UTC()
}
