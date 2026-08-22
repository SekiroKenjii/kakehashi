package store

import (
	"context"
	"database/sql"
	"errors"
	"time"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

// Every query against plugins.Plugin.

// ListListed returns the plugins the catalog offers, by name.
func (s *SQLServer) ListListed(ctx context.Context) ([]domain.Plugin, error) {
	const q = `
        SELECT p.PluginId, p.DisplayName, p.Description, p.Publisher, p.IsListed, p.CreatedAt, p.UpdatedAt
        FROM plugins.Plugin AS p
        WHERE p.IsListed = 1
        ORDER BY p.DisplayName ASC, p.PluginId ASC;`

	rows, err := s.db.QueryContext(ctx, q)
	if err != nil {
		return nil, errs.Internalf(err, "list plugins")
	}
	defer rows.Close()

	var plugins []domain.Plugin
	for rows.Next() {
		p, err := scanPlugin(rows)
		if err != nil {
			return nil, err
		}
		plugins = append(plugins, p)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list plugins")
	}
	return plugins, nil
}

// GetPlugin returns one plugin whether or not it is listed: an account that already installed it
// is entitled to an explanation, and hiding it here would turn that into a NOT_FOUND.
func (s *SQLServer) GetPlugin(ctx context.Context, pluginID string) (domain.Plugin, error) {
	const q = `
        SELECT p.PluginId, p.DisplayName, p.Description, p.Publisher, p.IsListed, p.CreatedAt, p.UpdatedAt
        FROM plugins.Plugin AS p
        WHERE p.PluginId = @p1;`

	p, err := scanPlugin(s.db.QueryRowContext(ctx, q, pluginID))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Plugin{}, errs.NotFoundf("No plugin called %q.", pluginID)
	}
	if err != nil {
		return domain.Plugin{}, err
	}
	return p, nil
}

// UpsertPlugin stores a plugin, rewriting what the catalog says about one it already has.
func (s *SQLServer) UpsertPlugin(ctx context.Context, p domain.Plugin) error {
	const q = `
        UPDATE plugins.Plugin
        SET DisplayName = @p2, Description = @p3, Publisher = @p4, UpdatedAt = @p6
        WHERE PluginId = @p1;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO plugins.Plugin (PluginId, DisplayName, Description, Publisher, IsListed, CreatedAt, UpdatedAt)
            VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p6);
        END;`

	_, err := s.db.ExecContext(
		ctx, q, p.PluginID, p.DisplayName, p.Description, p.Publisher, p.IsListed, storable(p.UpdatedAt))
	if err != nil {
		return errs.Internalf(err, "upsert plugin")
	}
	return nil
}

// SetListed shows or hides a plugin.
func (s *SQLServer) SetListed(ctx context.Context, pluginID string, listed bool, now time.Time) error {
	const q = `
        UPDATE plugins.Plugin
        SET IsListed = @p2, UpdatedAt = @p3
        WHERE PluginId = @p1;`

	result, err := s.db.ExecContext(ctx, q, pluginID, listed, storable(now))
	if err != nil {
		return errs.Internalf(err, "set plugin listed")
	}
	return requireOneRow(result, "No plugin called %q.", pluginID)
}
