package service

import (
	"context"
	"io"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
)

// Fetching an artifact.

// Download writes one version's bytes to w.
//
// A yanked version still downloads. Withdrawing stops the catalog offering it; an account that
// already has it installed must still be able to repair or reinstall, and refusing here would
// strand them with a plugin they cannot fix.
func (s *Service) Download(ctx context.Context, pluginID, version string, w io.Writer) error {
	if err := domain.ValidatePluginID(pluginID); err != nil {
		return err
	}

	if _, err := s.store.GetVersion(ctx, pluginID, version); err != nil {
		return err
	}
	return s.store.WriteContent(ctx, pluginID, version, w)
}
