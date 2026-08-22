package service

import (
	"context"

	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/eventbus"
)

// Recording what a client did with the catalog.

// RecordInstall stores that userID installed a version.
//
// The caller decides almost nothing. Who comes from the token, when comes from this server's
// clock, and the source is checked against a closed set — an open one would let a compromised
// client label a sideloaded package as one this catalog vetted, which is exactly the distinction a
// reader of the feed acts on.
//
// A version this catalog does not hold is refused for the same reason: without that check a client
// could assert a fact about a package nobody published.
func (s *Service) RecordInstall(ctx context.Context, userID, pluginID, version, source string) error {
	if userID == "" {
		return errs.Invalidf("An install belongs to an account.")
	}
	if err := domain.ValidatePluginID(pluginID); err != nil {
		return err
	}

	switch source {
	case pluginsapi.SourceCatalog, pluginsapi.SourceURL, pluginsapi.SourceFile:
	default:
		// Deliberately without the list: naming what is allowed teaches a caller what else to try.
		return errs.Invalidf("That is not an install source this server records.")
	}

	if _, err := s.store.GetVersion(ctx, pluginID, version); err != nil {
		return err
	}
	at := s.now().UTC()

	if err := s.store.InsertInstall(ctx, userID, pluginID, version, source, at); err != nil {
		return err
	}

	// Published after the write, never before. An event is a statement that something happened,
	// and an install that failed to record did not.
	eventbus.Publish(s.bus, ctx, pluginsapi.Installed{
		UserID:   userID,
		PluginID: pluginID,
		Version:  version,
		Source:   source,
		At:       at,
	})
	return nil
}
