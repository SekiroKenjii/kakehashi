package store

import (
	"context"
	"database/sql"
	"errors"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// RolePermission has no file of its own: the grants are entities inside the Role aggregate and are
// written with it.

// Every role, without their grants. The list screen shows a name, a description and two counts;
// loading every grant to render that would be five queries to answer a question none of them asks.
func (s *SQLServer) Roles(ctx context.Context) ([]domain.Role, error) {
	const q = `
        SELECT r.Id, r.Name, r.Description, r.IsSystem
        FROM authz.Role AS r
        ORDER BY r.IsSystem DESC, r.Name;`

	return collect(ctx, s.db, "list roles", q, nil, scanRole)
}

// One role, with its grants loaded — unlike Roles and RoleByName.
func (s *SQLServer) Role(ctx context.Context, id string) (domain.Role, error) {
	const q = `
        SELECT r.Id, r.Name, r.Description, r.IsSystem
        FROM authz.Role AS r
        WHERE r.Id = @p1;`

	role, err := scanRole(s.db.QueryRowContext(ctx, q, id))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Role{}, errs.NotFoundf("No role with id %s.", id)
	}
	if err != nil {
		return domain.Role{}, err
	}

	role.Grants, err = s.grantsOf(ctx, id)
	if err != nil {
		return domain.Role{}, err
	}
	return role, nil
}

// By display name, which is what the boot seed matches on.
func (s *SQLServer) RoleByName(ctx context.Context, name string) (domain.Role, error) {
	const q = `
        SELECT r.Id, r.Name, r.Description, r.IsSystem
        FROM authz.Role AS r
        WHERE r.Name = @p1;`

	role, err := scanRole(s.db.QueryRowContext(ctx, q, name))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Role{}, errs.NotFoundf("No role named %s.", name)
	}
	return role, err
}

func (s *SQLServer) InsertRole(ctx context.Context, r domain.Role, at time.Time) error {
	return s.inTransaction(ctx, func(tx *sql.Tx) error {
		const q = `
            INSERT INTO authz.Role (Id, Name, Description, IsSystem, CreatedAt, UpdatedAt)
            VALUES (@p1, @p2, @p3, @p4, @p5, @p5);`

		_, err := tx.ExecContext(ctx, q, r.ID, r.Name, r.Description, r.IsSystem, at.UTC())
		if err != nil {
			return errs.Internalf(err, "insert role")
		}
		return writeGrants(ctx, tx, r, "", at)
	})
}

// The name-collision check lives in the service, beside CreateRole's, so this only executes. A race
// that slips past it still hits AK_Role_Name and surfaces as an internal error — rare enough to
// accept, honest enough not to hide.
func (s *SQLServer) UpdateRole(ctx context.Context, r domain.Role) error {
	const q = `UPDATE authz.Role SET Name = @p1, Description = @p2 WHERE Id = @p3;`

	res, err := s.db.ExecContext(ctx, q, r.Name, r.Description, r.ID)
	if err != nil {
		return errs.Internalf(err, "update role")
	}
	if n, err := res.RowsAffected(); err == nil && n == 0 {
		return errs.NotFoundf("No role with id %s.", r.ID)
	}
	return nil
}

// Delete-then-insert rather than a diff, because what the administration screen sends is the whole
// set: they staged eight toggles and pressed Save once. A partial apply is a state nobody asked for
// and nobody can see, which is what the transaction is here to prevent.
func (s *SQLServer) SaveGrants(
	ctx context.Context, r domain.Role, actorID string, at time.Time,
) error {
	return s.inTransaction(ctx, func(tx *sql.Tx) error {
		const clear = `DELETE FROM authz.RolePermission WHERE RoleId = @p1;`
		if _, err := tx.ExecContext(ctx, clear, r.ID); err != nil {
			return errs.Internalf(err, "clear grants")
		}

		const touch = `UPDATE authz.Role SET UpdatedAt = @p2 WHERE Id = @p1;`
		if _, err := tx.ExecContext(ctx, touch, r.ID, at.UTC()); err != nil {
			return errs.Internalf(err, "touch role")
		}

		return writeGrants(ctx, tx, r, actorID, at)
	})
}

// Its grants and its assignments go with it, by cascade.
func (s *SQLServer) DeleteRole(ctx context.Context, id string) error {
	const q = `DELETE FROM authz.Role WHERE Id = @p1;`

	if _, err := s.db.ExecContext(ctx, q, id); err != nil {
		return errs.Internalf(err, "delete role")
	}
	return nil
}

// The query the request gate runs, and the only one on the hot path. One join rather than two round
// trips, and the widening happens in SQL because the answer is one row per permission either way —
// merging in Go would move the same data and then do the work twice.
//
// The widening is done on an explicit rank rather than on the scope string, because the string
// sorts the wrong way: alphabetically 'all' < 'own' < 'team', so MAX over the text picked 'team'
// over 'all' and an account holding two roles resolved to the narrower of them. Nothing tested the
// old claim that the two orders coincided, because the test that looked like it did was asserting
// auth.Widest, which ranks correctly in Go.
func (s *SQLServer) GrantsOfAccount(
	ctx context.Context, accountID string,
) (map[string]string, error) {
	const q = `
        SELECT rp.PermissionKey,
               MAX(CASE rp.Scope
                       WHEN N'all'  THEN 3
                       WHEN N'team' THEN 2
                       WHEN N'own'  THEN 1
                       ELSE 0
                   END) AS ScopeRank
        FROM authz.AccountRole AS ar
        INNER JOIN authz.RolePermission AS rp
            ON rp.RoleId = ar.RoleId
        WHERE ar.AccountId = @p1
        GROUP BY rp.PermissionKey;`

	rows, err := s.db.QueryContext(ctx, q, accountID)
	if err != nil {
		return nil, errs.Internalf(err, "resolve grants")
	}
	defer rows.Close()

	grants := map[string]string{}
	for rows.Next() {
		var (
			key  string
			rank int
		)
		if err := rows.Scan(&key, &rank); err != nil {
			return nil, errs.Internalf(err, "scan grant")
		}

		// Rank 0 means every row for this permission carried a scope this build does not know.
		// Skipped rather than defaulted: a grant nothing here understands must not silently become
		// the widest one.
		if scope := scopeOfRank(rank); scope != "" {
			grants[key] = scope
		}
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "resolve grants")
	}
	return grants, nil
}

func scopeOfRank(rank int) string {
	switch rank {
	case 3:
		return domain.ScopeAll
	case 2:
		return domain.ScopeTeam
	case 1:
		return domain.ScopeOwn
	default:
		return ""
	}
}

// Per role: permission count first, account count second.
func (s *SQLServer) CountsByRole(ctx context.Context) (map[string][2]int, error) {
	const q = `
        SELECT r.Id,
               (SELECT COUNT(*) FROM authz.RolePermission AS rp WHERE rp.RoleId = r.Id),
               (SELECT COUNT(*) FROM authz.AccountRole   AS ar WHERE ar.RoleId = r.Id)
        FROM authz.Role AS r;`

	rows, err := s.db.QueryContext(ctx, q)
	if err != nil {
		return nil, errs.Internalf(err, "count roles")
	}
	defer rows.Close()

	counts := map[string][2]int{}
	for rows.Next() {
		var id string
		var permissions, accounts int
		if err := rows.Scan(&id, &permissions, &accounts); err != nil {
			return nil, errs.Internalf(err, "scan role counts")
		}
		counts[id] = [2]int{permissions, accounts}
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "count roles")
	}
	return counts, nil
}

func (s *SQLServer) grantsOf(ctx context.Context, roleID string) (map[string]string, error) {
	const q = `
        SELECT rp.PermissionKey, rp.Scope
        FROM authz.RolePermission AS rp
        WHERE rp.RoleId = @p1;`

	rows, err := s.db.QueryContext(ctx, q, roleID)
	if err != nil {
		return nil, errs.Internalf(err, "list grants")
	}
	defer rows.Close()

	// Read as stored. No folding and no rank: there is nothing to widen across within a single
	// role, which is why this differs from GrantsOfAccount and why a change there must not be
	// copied here.
	grants := map[string]string{}
	for rows.Next() {
		var key, scope string
		if err := rows.Scan(&key, &scope); err != nil {
			return nil, errs.Internalf(err, "scan grant")
		}
		grants[key] = scope
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list grants")
	}
	return grants, nil
}

func writeGrants(ctx context.Context, tx *sql.Tx, r domain.Role, actorID string, at time.Time) error {
	const q = `
        INSERT INTO authz.RolePermission (RoleId, PermissionKey, Scope, GrantedBy, GrantedAt)
        VALUES (@p1, @p2, @p3, @p4, @p5);`

	for key, scope := range r.Grants {
		if _, err := tx.ExecContext(ctx, q, r.ID, key, scope, actorID, at.UTC()); err != nil {
			return errs.Internalf(err, "insert grant %s", key)
		}
	}
	return nil
}

func scanRole(sc scanner) (domain.Role, error) {
	var r domain.Role
	if err := sc.Scan(&r.ID, &r.Name, &r.Description, &r.IsSystem); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			// Handed back untouched: only the caller knows which role was looked for.
			return domain.Role{}, err
		}
		return domain.Role{}, errs.Internalf(err, "scan role")
	}
	r.Grants = map[string]string{}
	return r, nil
}
