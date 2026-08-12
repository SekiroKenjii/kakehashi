package store

import (
	"context"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

func (s *SQLServer) RolesOf(ctx context.Context, accountID string) ([]domain.Role, error) {
	const q = `
        SELECT r.Id, r.Name, r.Description, r.IsSystem
        FROM authz.AccountRole AS ar
        INNER JOIN authz.Role AS r
            ON r.Id = ar.RoleId
        WHERE ar.AccountId = @p1
        ORDER BY r.IsSystem DESC, r.Name;`

	return collect(ctx, s.db, "list account roles", q, []any{accountID}, scanRole)
}

// Many accounts at once, so the user list does not run one query per row. An account with no roles
// is absent from the map rather than present and empty.
func (s *SQLServer) RolesOfAccounts(
	ctx context.Context, accountIDs []string,
) (map[string][]domain.Role, error) {
	// An empty list asks about everybody. That is the call the users screen makes — it is already
	// listing every account — so answering with an empty map would answer a different question.
	where := ""
	args := make([]any, len(accountIDs))
	for i, id := range accountIDs {
		args[i] = id
	}
	if len(args) > 0 {
		where = "WHERE ar.AccountId IN (" + placeholders(len(args)) + ")"
	}

	q := `
        SELECT ar.AccountId, r.Id, r.Name, r.Description, r.IsSystem
        FROM authz.AccountRole AS ar
        INNER JOIN authz.Role AS r
            ON r.Id = ar.RoleId
        ` + where + `
        ORDER BY r.IsSystem DESC, r.Name;`

	rows, err := s.db.QueryContext(ctx, q, args...)
	if err != nil {
		return nil, errs.Internalf(err, "list roles for accounts")
	}
	defer rows.Close()

	byAccount := map[string][]domain.Role{}
	for rows.Next() {
		var accountID string
		var r domain.Role
		if err := rows.Scan(&accountID, &r.ID, &r.Name, &r.Description, &r.IsSystem); err != nil {
			return nil, errs.Internalf(err, "scan account role")
		}
		r.Grants = map[string]string{}
		byAccount[accountID] = append(byAccount[accountID], r)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list roles for accounts")
	}
	return byAccount, nil
}

// Whether the account would still hold the permission if the named role stopped granting it —
// asked before every destructive change on this module's own surface, to catch an administrator
// about to lock themselves out.
//
// UPDLOCK and HOLDLOCK because the answer is acted on. Without them this is an ordinary read: two
// administrators each removing their own last grant both see the other's still there, both are told
// they are safe, and both writes land — leaving a deployment with nobody able to grant anything.
// The hint holds the rows until the transaction that asked commits, so the second one waits and
// then gets the true answer.
func (s *SQLServer) HoldsPermissionWithoutRole(
	ctx context.Context, accountID, permissionKey, excludedRoleID string,
) (bool, error) {
	const q = `
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM authz.AccountRole AS ar WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN authz.RolePermission AS rp WITH (UPDLOCK, HOLDLOCK)
                ON rp.RoleId = ar.RoleId
            WHERE ar.AccountId = @p1
              AND rp.PermissionKey = @p2
              AND ar.RoleId <> @p3
        ) THEN 1 ELSE 0 END;`

	var holds bool
	err := s.db.QueryRowContext(ctx, q, accountID, permissionKey, excludedRoleID).Scan(&holds)
	if err != nil {
		return false, errs.Internalf(err, "check permission without role")
	}
	return holds, nil
}

// Re-assigning a role the account already holds leaves the original row, including who assigned it
// first — which is the answer an access review wants.
func (s *SQLServer) AssignRole(
	ctx context.Context, accountID, roleID, by string, at time.Time,
) error {
	const q = `
        IF NOT EXISTS (
            SELECT 1
            FROM authz.AccountRole AS ar
            WHERE ar.AccountId = @p1 AND ar.RoleId = @p2
        )
        BEGIN
            INSERT INTO authz.AccountRole (AccountId, RoleId, AssignedBy, AssignedAt)
            VALUES (@p1, @p2, @p3, @p4);
        END;`

	if _, err := s.db.ExecContext(ctx, q, accountID, roleID, by, at.UTC()); err != nil {
		return errs.Internalf(err, "assign role")
	}
	return nil
}

func (s *SQLServer) UnassignRole(ctx context.Context, accountID, roleID string) error {
	const q = `
        DELETE FROM authz.AccountRole
        WHERE AccountId = @p1 AND RoleId = @p2;`

	if _, err := s.db.ExecContext(ctx, q, accountID, roleID); err != nil {
		return errs.Internalf(err, "unassign role")
	}
	return nil
}
