package store

import (
	"context"
	"database/sql"
	"strconv"
	"strings"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Unpaged, because the count is bounded by how many permissions are compiled into a build — a
// number a person maintains by hand.
func (s *SQLServer) Permissions(ctx context.Context) ([]domain.Permission, error) {
	const q = `
        SELECT p.PermissionKey, p.Name, p.Description, p.Category, p.IsHighRisk, p.IsScoped
        FROM authz.Permission AS p
        ORDER BY p.Category, p.Name;`

	return collect(ctx, s.db, "list permissions", q, nil, scanPermission)
}

// Deleting is the half that matters: a permission left behind by a module somebody removed still
// appears on the administration screen, still looks grantable, and grants nothing — worse than not
// being there, because someone will grant it and believe they did something.
//
// Grants are deliberately not touched. They have no foreign key to this table, so a grant naming a
// permission that has gone quiet stays inert and comes back to life if the module returns.
func (s *SQLServer) ReconcilePermissions(ctx context.Context, declared []domain.Permission) error {
	return s.inTransaction(ctx, func(tx *sql.Tx) error {
		const upsert = `
            MERGE authz.Permission AS target
            USING (SELECT @p1 AS PermissionKey) AS source
                ON target.PermissionKey = source.PermissionKey
            WHEN MATCHED THEN
                UPDATE SET Name = @p2, Description = @p3, Category = @p4, IsHighRisk = @p5,
                           IsScoped = @p6
            WHEN NOT MATCHED THEN
                INSERT (PermissionKey, Name, Description, Category, IsHighRisk, IsScoped)
                VALUES (@p1, @p2, @p3, @p4, @p5, @p6);`

		keys := make([]any, 0, len(declared))
		for _, p := range declared {
			_, err := tx.ExecContext(
				ctx, upsert, p.Key, p.Name, p.Description, p.Category, p.IsHighRisk, p.IsScoped)
			if err != nil {
				return errs.Internalf(err, "upsert permission %s", p.Key)
			}
			keys = append(keys, p.Key)
		}

		// Nothing declared means no module is mounted — a boot so broken that emptying the
		// catalogue would be the least of it. Leave the table alone and let the caller notice.
		if len(keys) == 0 {
			return nil
		}

		remove := `DELETE FROM authz.Permission WHERE PermissionKey NOT IN (` + placeholders(len(keys)) + `);`
		if _, err := tx.ExecContext(ctx, remove, keys...); err != nil {
			return errs.Internalf(err, "prune permissions")
		}
		return nil
	})
}

func scanPermission(sc scanner) (domain.Permission, error) {
	var p domain.Permission
	err := sc.Scan(&p.Key, &p.Name, &p.Description, &p.Category, &p.IsHighRisk, &p.IsScoped)
	if err != nil {
		return domain.Permission{}, errs.Internalf(err, "scan permission")
	}
	return p, nil
}

// go-mssqldb has no array binding. Building the IN list is safe because only the count comes from
// the caller — every value still binds.
func placeholders(n int) string {
	parts := make([]string, n)
	for i := range parts {
		parts[i] = "@p" + strconv.Itoa(i+1)
	}
	return strings.Join(parts, ", ")
}
