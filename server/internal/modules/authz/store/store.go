// Package store persists roles, permissions and grants. It is private to the authz module.
//
// Every table lives in the authz schema, which the kernel creates before the first migration runs.
//
// The files: this one is the seam, migrations.go is the schema history, and there is one file per
// table — permission.go, role.go (which owns RolePermission, because the grants are entities inside
// the Role aggregate and are written with it), accountrole.go and audit.go.
package store

import (
	"context"
	"database/sql"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/database"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// SQLServer stores authorization state in the shared database.
type SQLServer struct {
	db *database.DB
}

func New(db *database.DB) *SQLServer { return &SQLServer{db: db} }

// scanner is what *sql.Row and *sql.Rows have in common, so one scan serves both.
type scanner interface {
	Scan(dest ...any) error
}

// collect runs a query and maps every row, so the six list methods in this package do not each
// repeat the same six lines of cursor handling.
func collect[T any](
	ctx context.Context, db *database.DB, what, query string, args []any,
	scan func(scanner) (T, error),
) ([]T, error) {
	rows, err := db.QueryContext(ctx, query, args...)
	if err != nil {
		return nil, errs.Internalf(err, "%s", what)
	}
	defer rows.Close()

	var out []T
	for rows.Next() {
		item, err := scan(rows)
		if err != nil {
			return nil, err
		}
		out = append(out, item)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "%s", what)
	}
	return out, nil
}

// inTransaction runs fn against a transaction, rolling back on any error.
//
// The one place this module needs a transaction, and it needs it for the reason the Role aggregate
// exists: an administrator saving eight toggles is composing one decision, and a save that applied
// four of them is a state nobody asked for.
func (s *SQLServer) inTransaction(ctx context.Context, fn func(*sql.Tx) error) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return errs.Internalf(err, "begin transaction")
	}

	if err := fn(tx); err != nil {
		_ = tx.Rollback()
		return err
	}
	if err := tx.Commit(); err != nil {
		return errs.Internalf(err, "commit transaction")
	}
	return nil
}
