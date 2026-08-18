// Package store is the health module's persistence seam, and it holds no persistence: no tables,
// no migrations, only the liveness probes for the stores the process depends on. It exists as
// store/ because probing a database is touching one, and only this package in the module may —
// the same archlint rule that keeps queries out of every other layer keeps pings out too.
package store

import (
	"context"

	"__GO_MODULE__/server/internal/platform/database"
	"__GO_MODULE__/server/internal/platform/mongodb"
)

// Store probes the two stores every module shares.
type Store struct {
	sql   *database.DB
	mongo *mongodb.DB
}

// New returns a store probing both handles.
func New(sql *database.DB, mongo *mongodb.DB) *Store {
	return &Store{sql: sql, mongo: mongo}
}

// PingSQL answers whether SQL Server responds.
func (s *Store) PingSQL(ctx context.Context) error {
	return s.sql.PingContext(ctx)
}

// PingMongo answers whether MongoDB responds.
func (s *Store) PingMongo(ctx context.Context) error {
	return s.mongo.Ping(ctx)
}
