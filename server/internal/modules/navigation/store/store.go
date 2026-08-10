// Package store persists where a client's destinations sit. It is private to the navigation module.
//
// Every table lives in the navigation schema, which the kernel creates before the first migration
// runs.
//
// The files: this one is the seam, migrations.go is the schema history, group.go owns the headings
// and item.go owns the placements.
//
// Note what this package does NOT store: which destinations exist, and what protects them. Both are
// declared in code and arrive as arguments. A table that could add a destination would be a table
// that could add an unprotected page.
package store

import (
	"context"
	"database/sql"
	"strings"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/database"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// SQLServer stores navigation layout in the shared database.
type SQLServer struct {
	db *database.DB
}

// New returns a store backed by db.
func New(db *database.DB) *SQLServer { return &SQLServer{db: db} }

// scanner is what *sql.Row and *sql.Rows have in common, so one scan serves both.
type scanner interface {
	Scan(dest ...any) error
}

// collect runs a query and maps every row, so the list methods here do not each repeat the same
// cursor handling.
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
// Reconcile is the reason it exists: seeding the headings and then the placements that point at them
// is one act, and a boot that created the headings and failed before the placements would leave a
// pane arranged out of nothing.
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

// isUniqueViolation reports whether err is SQL Server complaining about a unique index.
//
// Matching on the message rather than the driver's error type, for the reason the account module's
// store gives: go-mssqldb exposes the number only through a concrete type this package would then
// have to import and assert on, and the wording is stable across every version anyone runs.
func isUniqueViolation(err error) bool {
	if err == nil {
		return false
	}

	text := err.Error()
	return strings.Contains(text, "Cannot insert duplicate key") ||
		strings.Contains(text, "Violation of UNIQUE KEY") ||
		strings.Contains(text, "Violation of PRIMARY KEY")
}

// errorContains reports whether the driver's message mentions text.
func errorContains(err error, text string) bool {
	return err != nil && strings.Contains(err.Error(), text)
}

// Layout reads the headings and the placements as one consistent snapshot.
//
// One transaction, because the two halves are read together and interpreted together: a placement
// names a heading, and a heading created between two independent reads produced a snapshot whose
// placements pointed at something the groups half did not contain. Build drops a destination whose
// heading it cannot find, so the symptom was a screen missing from every pane until the next write
// happened to invalidate the cache.
func (s *SQLServer) Layout(ctx context.Context) ([]domain.Group, []domain.Placement, error) {
	// An ordinary transaction, not a read-only one: go-mssqldb refuses ReadOnly outright, and the
	// guarantee wanted here is one point in time rather than a promise not to write.
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return nil, nil, errs.Internalf(err, "read navigation layout")
	}
	defer func() { _ = tx.Rollback() }()

	groups, err := collectTx(ctx, tx, "list navigation groups", groupsQuery, scanGroup)
	if err != nil {
		return nil, nil, err
	}
	placements, err := collectTx(ctx, tx, "list navigation items", placementsQuery, scanPlacement)
	if err != nil {
		return nil, nil, err
	}
	return groups, placements, nil
}

// collectTx is collect, against a transaction. Two functions rather than one because database/sql
// gives *sql.DB and *sql.Tx no common interface worth naming for two call sites.
func collectTx[T any](
	ctx context.Context, tx *sql.Tx, what, query string, scan func(scanner) (T, error),
) ([]T, error) {
	rows, err := tx.QueryContext(ctx, query)
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
