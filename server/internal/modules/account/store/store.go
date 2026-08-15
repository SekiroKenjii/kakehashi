// Package store persists accounts, sessions and the OpenID Connect provider's state. It is private
// to the account module.
//
// Every table it touches lives in the account schema, which the kernel creates before the first
// migration runs. Style follows ktaranov/sqlserver-kit — see platform/database. Two names come
// from that guide: the entity table is Account (USER is reserved) and the session table is
// UserSession (SESSION is ODBC-reserved).
//
// The provider's state is in the database rather than in memory (zitadel/oidc's example keeps it
// in memory) because an in-memory store signs every user out on restart: the refresh token is
// what survives a client reboot, and losing it turns a deploy into a forced re-login for everyone.
package store

import (
	"context"
	"database/sql"
	"strings"
	"time"

	"__GO_MODULE__/server/internal/platform/database"
	"__GO_MODULE__/server/internal/platform/errs"
)

// SQLServer stores account state in the shared database.
type SQLServer struct {
	db *database.DB
}

// New returns a store backed by db.
func New(db *database.DB) *SQLServer { return &SQLServer{db: db} }

// Health reports whether the database answers. The provider's discovery of "is storage alive"
// funnels through here.
func (s *SQLServer) Health(ctx context.Context) error {
	if err := s.db.PingContext(ctx); err != nil {
		return errs.Internalf(err, "ping database")
	}
	return nil
}

// scanner is what *sql.Row and *sql.Rows have in common.
type scanner interface {
	Scan(dest ...any) error
}

// storable rounds a timestamp to what a datetime2(3) column holds, in UTC, so a value read back
// equals the value written.
func storable(t time.Time) time.Time {
	return t.UTC().Truncate(time.Millisecond)
}

// nullable maps the empty string to SQL NULL, which is what the filtered unique indexes above
// expect: "no refresh token" must not collide with another row that also has none.
func nullable(s string) any {
	if s == "" {
		return nil
	}
	return s
}

func requireOneRow(res sql.Result, format string, args ...any) error {
	affected, err := res.RowsAffected()
	if err != nil {
		return errs.Internalf(err, "read affected rows")
	}
	if affected == 0 {
		return errs.NotFoundf(format, args...)
	}
	return nil
}

// isUniqueViolation reports whether err is SQL Server complaining about a unique index.
//
// Matching on the message rather than the driver's error type: go-mssqldb exposes the number, but
// only through a concrete type this package would then have to import and assert on, and 2601/2627
// are stable across every version anyone runs.
func isUniqueViolation(err error) bool {
	text := err.Error()
	return strings.Contains(text, "Cannot insert duplicate key") ||
		strings.Contains(text, "Violation of UNIQUE KEY") ||
		strings.Contains(text, "Violation of PRIMARY KEY")
}
