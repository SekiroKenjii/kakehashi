package store

import (
	"context"
	"time"

	"__GO_MODULE__/server/internal/platform/errs"
)

// Every query against plugins.PluginInstall.

// InsertInstall records that an account installed a version.
//
// Append-only, and reinstalling writes a second row rather than updating the first: the question a
// reader asks of this table is when something happened, and a row that moves cannot answer it.
func (s *SQLServer) InsertInstall(
	ctx context.Context, userID, pluginID, version, source string, at time.Time,
) error {
	const q = `
        INSERT INTO plugins.PluginInstall (UserId, PluginId, Version, Source, InstalledAt)
        VALUES (@p1, @p2, @p3, @p4, @p5);`

	_, err := s.db.ExecContext(ctx, q, userID, pluginID, version, source, storable(at))
	if err != nil {
		return errs.Internalf(err, "insert plugin install")
	}
	return nil
}
