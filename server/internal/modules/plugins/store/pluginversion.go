package store

import (
	"context"
	"database/sql"
	"errors"
	"io"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

// Every query against plugins.PluginVersion. Its own file because the store's unit is the table,
// even though the domain keeps a version inside the plugin it belongs to.

// downloadChunkBytes is how much of an artifact one read pulls back.
//
// SUBSTRING over varbinary(max) rather than one read of the column: the whole point of streaming
// the download is that neither the server nor the client holds the package in memory, and reading
// the column whole would put it back.
const downloadChunkBytes = 256 << 10

// LatestVersions returns the newest version on offer for each of the given plugins, keyed by
// plugin id. A plugin whose every version is yanked is absent rather than present and empty.
func (s *SQLServer) LatestVersions(ctx context.Context) (map[string]domain.Version, error) {
	const q = `
        SELECT p.PluginId, v.Version, v.MinHostSdk, v.SizeInBytes, v.Sha256, v.IsYanked, v.PublishedAt
        FROM plugins.PluginVersion AS v
        INNER JOIN plugins.Plugin AS p ON p.Id = v.PluginId
        WHERE v.IsYanked = 0
            AND v.Id = (
                SELECT TOP 1 newest.Id
                FROM plugins.PluginVersion AS newest
                WHERE newest.PluginId = v.PluginId AND newest.IsYanked = 0
                ORDER BY newest.PublishedAt DESC, newest.Id DESC
            );`

	rows, err := s.db.QueryContext(ctx, q)
	if err != nil {
		return nil, errs.Internalf(err, "list latest plugin versions")
	}
	defer rows.Close()

	latest := make(map[string]domain.Version)
	for rows.Next() {
		v, err := scanVersion(rows)
		if err != nil {
			return nil, err
		}
		latest[v.PluginID] = v
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list latest plugin versions")
	}
	return latest, nil
}

// ListVersions returns every version of a plugin, newest first, yanked ones included: the catalog
// stops offering a withdrawn version, and an administrator still has to see it to put it back.
func (s *SQLServer) ListVersions(ctx context.Context, pluginID string) ([]domain.Version, error) {
	const q = `
        SELECT p.PluginId, v.Version, v.MinHostSdk, v.SizeInBytes, v.Sha256, v.IsYanked, v.PublishedAt
        FROM plugins.PluginVersion AS v
        INNER JOIN plugins.Plugin AS p ON p.Id = v.PluginId
        WHERE p.PluginId = @p1
        ORDER BY v.PublishedAt DESC, v.Id DESC;`

	rows, err := s.db.QueryContext(ctx, q, pluginID)
	if err != nil {
		return nil, errs.Internalf(err, "list plugin versions")
	}
	defer rows.Close()

	var versions []domain.Version
	for rows.Next() {
		v, err := scanVersion(rows)
		if err != nil {
			return nil, err
		}
		versions = append(versions, v)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list plugin versions")
	}
	return versions, nil
}

// GetVersion returns one version's metadata.
func (s *SQLServer) GetVersion(ctx context.Context, pluginID, version string) (domain.Version, error) {
	const q = `
        SELECT p.PluginId, v.Version, v.MinHostSdk, v.SizeInBytes, v.Sha256, v.IsYanked, v.PublishedAt
        FROM plugins.PluginVersion AS v
        INNER JOIN plugins.Plugin AS p ON p.Id = v.PluginId
        WHERE p.PluginId = @p1 AND v.Version = @p2;`

	v, err := scanVersion(s.db.QueryRowContext(ctx, q, pluginID, version))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Version{}, errs.NotFoundf("No version %s of %q.", version, pluginID)
	}
	if err != nil {
		return domain.Version{}, err
	}
	return v, nil
}

// InsertVersion stores a package. A version this plugin already has fails as invalid rather than
// overwriting: a published artifact is what somebody's checksum refers to.
func (s *SQLServer) InsertVersion(ctx context.Context, v domain.Version, content []byte) error {
	const q = `
        INSERT INTO plugins.PluginVersion
            (PluginId, Version, MinHostSdk, SizeInBytes, Sha256, Content, IsYanked, PublishedAt)
        SELECT p.Id, @p2, @p3, @p4, @p5, @p6, 0, @p7
        FROM plugins.Plugin AS p
        WHERE p.PluginId = @p1;`

	res, err := s.db.ExecContext(
		ctx, q, v.PluginID, v.Version, v.MinHostSDK, v.SizeInBytes, v.SHA256, content,
		storable(v.PublishedAt))
	if err != nil {
		if isUniqueViolation(err) {
			return errs.Invalidf("Version %s of %q is already published.", v.Version, v.PluginID)
		}
		return errs.Internalf(err, "insert plugin version")
	}
	return requireOneRow(res, "No plugin called %q.", v.PluginID)
}

// SetYanked withdraws a version, or puts it back.
func (s *SQLServer) SetYanked(ctx context.Context, pluginID, version string, yanked bool) error {
	const q = `
        UPDATE v
        SET v.IsYanked = @p3
        FROM plugins.PluginVersion AS v
        INNER JOIN plugins.Plugin AS p ON p.Id = v.PluginId
        WHERE p.PluginId = @p1 AND v.Version = @p2;`

	res, err := s.db.ExecContext(ctx, q, pluginID, version, yanked)
	if err != nil {
		return errs.Internalf(err, "set plugin version yanked")
	}
	return requireOneRow(res, "No version %s of %q.", version, pluginID)
}

// WriteContent streams one version's bytes to w.
func (s *SQLServer) WriteContent(ctx context.Context, pluginID, version string, w io.Writer) error {
	const q = `
        SELECT SUBSTRING(v.Content, @p3, @p4)
        FROM plugins.PluginVersion AS v
        INNER JOIN plugins.Plugin AS p ON p.Id = v.PluginId
        WHERE p.PluginId = @p1 AND v.Version = @p2;`

	/* SUBSTRING over varbinary is 1-based, so the first byte is at offset 1, not 0. */
	for offset := int64(1); ; offset += downloadChunkBytes {
		var chunk []byte

		err := s.db.QueryRowContext(ctx, q, pluginID, version, offset, downloadChunkBytes).Scan(&chunk)
		if errors.Is(err, sql.ErrNoRows) {
			return errs.NotFoundf("No version %s of %q.", version, pluginID)
		}
		if err != nil {
			return errs.Internalf(err, "read plugin version content")
		}
		if len(chunk) == 0 {
			return nil
		}
		if _, err := w.Write(chunk); err != nil {
			return err
		}
		if len(chunk) < downloadChunkBytes {
			return nil
		}
	}
}
