// Package store persists notes, and is private to the notes module.
//
// Every table it touches lives in the notes schema, which the kernel creates before the first
// migration runs. tools/archlint can check that only this package imports the database; it cannot
// read the SQL, so writing outside the module's own schema stays a review rule.
//
// Style follows ktaranov/sqlserver-kit — see platform/database. One query file per table: a second
// table gets a second one rather than more of note.go.
package store

import (
	"database/sql"
	"errors"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/database"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

type SQLServer struct {
	db *database.DB
}

func New(db *database.DB) *SQLServer { return &SQLServer{db: db} }

// scanner is what *sql.Row and *sql.Rows have in common, so one scan serves both.
type scanner interface {
	Scan(dest ...any) error
}

func scanNote(sc scanner) (domain.Note, error) {
	var n domain.Note

	if err := sc.Scan(&n.ID, &n.Title, &n.Body, &n.CreatedAt, &n.UpdatedAt); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			// Handed back untouched: only the caller knows which ID was looked for, so only the
			// caller can write a useful message.
			return domain.Note{}, err
		}
		return domain.Note{}, errs.Internalf(err, "scan note")
	}

	// DATETIME2 carries no time zone, so the driver hands back a time.Time whose location is
	// whatever it defaulted to. Only UTC is ever written, so say so rather than letting a local
	// zone be inferred from a value that never had one.
	n.CreatedAt = n.CreatedAt.UTC()
	n.UpdatedAt = n.UpdatedAt.UTC()
	return n, nil
}

// storable rounds down to the precision of the DATETIME2(3) columns, in UTC.
func storable(t time.Time) time.Time {
	return t.UTC().Truncate(time.Millisecond)
}
