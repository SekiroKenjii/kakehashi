// Package store persists the plugin catalog. It is private to the plugins module.
//
// Every table it touches lives in the plugins schema, which the kernel creates before the first
// migration runs. tools/archlint can check that only this package imports the database; it cannot
// read the SQL, so writing outside the module's own schema stays a review rule.
//
// Style follows ktaranov/sqlserver-kit — see platform/database.
//
// The files: this one is the seam, holding the type, its constructor and the helpers more than one
// query needs. migrations.go holds the schema history, which is one unit because its value is its
// order. Then one file per table — plugin.go, pluginversion.go, install.go — because the store's
// unit is the table, and a version is its own table even though the domain keeps it inside the
// plugin it belongs to.
package store

import (
	"database/sql"
	"errors"
	"strings"
	"time"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/database"
	"__GO_MODULE__/server/internal/platform/errs"
)

// SQLServer stores the catalog in the shared database.
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

func scanPlugin(sc scanner) (domain.Plugin, error) {
	var p domain.Plugin

	err := sc.Scan(
		&p.PluginID, &p.DisplayName, &p.Description, &p.Publisher,
		&p.IsListed, &p.CreatedAt, &p.UpdatedAt)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			// Hand this back untouched: only the caller knows which plugin was being looked for,
			// so only the caller can write a useful message.
			return domain.Plugin{}, err
		}
		return domain.Plugin{}, errs.Internalf(err, "scan plugin")
	}
	p.CreatedAt = p.CreatedAt.UTC()
	p.UpdatedAt = p.UpdatedAt.UTC()
	return p, nil
}

func scanVersion(sc scanner) (domain.Version, error) {
	var v domain.Version

	err := sc.Scan(
		&v.PluginID, &v.Version, &v.MinHostSDK, &v.SizeInBytes,
		&v.SHA256, &v.IsYanked, &v.PublishedAt)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return domain.Version{}, err
		}
		return domain.Version{}, errs.Internalf(err, "scan plugin version")
	}
	v.PublishedAt = v.PublishedAt.UTC()
	return v, nil
}

// storable rounds a timestamp down to the precision of a DATETIME2(3) column, in UTC.
func storable(t time.Time) time.Time {
	return t.UTC().Truncate(time.Millisecond)
}

// requireOneRow turns "the UPDATE matched nothing" into the not-found it means. It sits on the
// seam because it operates on no table of its own and more than one file needs it.
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
	if err == nil {
		return false
	}
	message := err.Error()
	return strings.Contains(message, "Violation of UNIQUE KEY constraint") ||
		strings.Contains(message, "Cannot insert duplicate key")
}
