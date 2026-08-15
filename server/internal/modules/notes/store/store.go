// Package store persists notes. It is private to the notes module.
//
// Every table it touches lives in the notes schema, which the kernel creates before the first
// migration runs. tools/archlint can check that only this package imports the database; it cannot
// read the SQL, so writing outside the module's own schema stays a review rule.
//
// Style follows ktaranov/sqlserver-kit — see platform/database.
//
// The files: this one is the seam, holding the type, its constructor and the helpers more than one
// query needs. migrations.go holds the schema history, which is one unit because its value is its
// order. note.go holds the queries against the one table. The module has a single table today, so
// there is a single query file; a second table gets a second one rather than more of this one.
package store

import (
	"database/sql"
	"errors"
	"time"

	"__GO_MODULE__/server/internal/modules/notes/domain"
	"__GO_MODULE__/server/internal/platform/database"
	"__GO_MODULE__/server/internal/platform/errs"
)

// SQLServer stores notes in the shared database.
type SQLServer struct {
	db *database.DB
}

// New returns a store backed by db.
func New(db *database.DB) *SQLServer { return &SQLServer{db: db} }

// scanner is what *sql.Row and *sql.Rows have in common, so one scan function serves both the
// single-row and the many-row queries.
type scanner interface {
	Scan(dest ...any) error
}

func scanNote(sc scanner) (domain.Note, error) {
	var n domain.Note

	if err := sc.Scan(&n.ID, &n.Title, &n.Body, &n.CreatedAt, &n.UpdatedAt); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			// Hand this back untouched: only the caller knows which ID was being looked for, so
			// only the caller can write a useful message.
			return domain.Note{}, err
		}
		return domain.Note{}, errs.Internalf(err, "scan note")
	}

	// DATETIME2 carries no zone, so the driver returns whatever location it defaulted to. Only UTC
	// is ever written, so say so rather than let a local zone be inferred.
	n.CreatedAt = n.CreatedAt.UTC()
	n.UpdatedAt = n.UpdatedAt.UTC()
	return n, nil
}

// storable rounds a timestamp down to the precision of a DATETIME2(3) column, in UTC.
func storable(t time.Time) time.Time {
	return t.UTC().Truncate(time.Millisecond)
}
