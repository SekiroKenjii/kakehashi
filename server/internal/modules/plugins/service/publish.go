package service

import (
	"context"
	"crypto/sha256"
	"encoding/hex"

	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/eventbus"
)

// Changing what the catalog holds.

// Publish stores an uploaded package.
//
// The digest the uploader sent is checked against the bytes that arrived rather than trusted, so
// the value the catalog serves to every client afterwards is one this server computed. A client
// that hashes what it downloaded and compares is then checking the same number end to end.
func (s *Service) Publish(
	ctx context.Context, plugin pluginsapi.Plugin, version pluginsapi.Version, content []byte,
) (pluginsapi.Version, error) {
	now := s.now().UTC()

	entity, err := domain.NewPlugin(
		plugin.PluginID, plugin.DisplayName, plugin.Description, plugin.Publisher, now)
	if err != nil {
		return pluginsapi.Version{}, err
	}

	next, err := domain.NewVersion(
		version.PluginID, version.Version, version.MinHostSDK, version.SHA256,
		int64(len(content)), pluginsapi.MaxPackageBytes, now)
	if err != nil {
		return pluginsapi.Version{}, err
	}

	if entity.PluginID != next.PluginID {
		return pluginsapi.Version{}, errs.Invalidf("A version belongs to the plugin it names.")
	}
	digest := sha256.Sum256(content)

	if hex.EncodeToString(digest[:]) != next.SHA256 {
		return pluginsapi.Version{}, errs.Invalidf(
			"The package does not match the digest that was sent with it.")
	}

	// The plugin row first: a version has nowhere to hang without it, and re-publishing an
	// existing plugin is how its description is kept current.
	if existing, err := s.store.GetPlugin(ctx, entity.PluginID); err == nil {
		if err := existing.Describe(entity.DisplayName, entity.Description, entity.Publisher, now); err != nil {
			return pluginsapi.Version{}, err
		}
		entity = existing
	} else if errs.KindOf(err) != errs.NotFound {
		return pluginsapi.Version{}, err
	}

	if err := s.store.UpsertPlugin(ctx, entity); err != nil {
		return pluginsapi.Version{}, err
	}

	if err := s.store.InsertVersion(ctx, next, content); err != nil {
		return pluginsapi.Version{}, err
	}
	eventbus.Publish(s.bus, ctx, pluginsapi.Published{
		PluginID: next.PluginID,
		Version:  next.Version,
		At:       now,
	})
	return toAPIVersion(next), nil
}

// SetYanked withdraws a version, or puts it back.
func (s *Service) SetYanked(ctx context.Context, pluginID, version string, yanked bool) error {
	if err := domain.ValidatePluginID(pluginID); err != nil {
		return err
	}
	return s.store.SetYanked(ctx, pluginID, version, yanked)
}

// SetListed shows or hides a whole plugin.
func (s *Service) SetListed(ctx context.Context, pluginID string, listed bool) error {
	if err := domain.ValidatePluginID(pluginID); err != nil {
		return err
	}
	return s.store.SetListed(ctx, pluginID, listed, s.now().UTC())
}
