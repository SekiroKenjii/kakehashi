package service

import (
	"context"

	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/modules/plugins/domain"
)

// Reading what the catalog offers.

// List returns the listed plugins that have at least one version on offer.
//
// A plugin whose every version is yanked is left out rather than shown with nothing to install:
// the row exists so somebody can install it, and one that cannot be is a dead end.
func (s *Service) List(ctx context.Context) ([]pluginsapi.Listing, error) {
	plugins, err := s.store.ListListed(ctx)
	if err != nil {
		return nil, err
	}

	latest, err := s.store.LatestVersions(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]pluginsapi.Listing, 0, len(plugins))
	for _, p := range plugins {
		v, ok := latest[p.PluginID]
		if !ok {
			continue
		}
		out = append(out, pluginsapi.Listing{Plugin: toAPIPlugin(p), Latest: toAPIVersion(v)})
	}
	return out, nil
}

// Get returns one plugin and its versions, newest first.
func (s *Service) Get(ctx context.Context, pluginID string) (pluginsapi.Plugin, []pluginsapi.Version, error) {
	if err := domain.ValidatePluginID(pluginID); err != nil {
		return pluginsapi.Plugin{}, nil, err
	}

	p, err := s.store.GetPlugin(ctx, pluginID)
	if err != nil {
		return pluginsapi.Plugin{}, nil, err
	}

	versions, err := s.store.ListVersions(ctx, pluginID)
	if err != nil {
		return pluginsapi.Plugin{}, nil, err
	}

	out := make([]pluginsapi.Version, len(versions))
	for i, v := range versions {
		out[i] = toAPIVersion(v)
	}
	return toAPIPlugin(p), out, nil
}

func toAPIPlugin(p domain.Plugin) pluginsapi.Plugin {
	return pluginsapi.Plugin{
		PluginID:    p.PluginID,
		DisplayName: p.DisplayName,
		Description: p.Description,
		Publisher:   p.Publisher,
		IsListed:    p.IsListed,
		CreatedAt:   p.CreatedAt,
		UpdatedAt:   p.UpdatedAt,
	}
}

func toAPIVersion(v domain.Version) pluginsapi.Version {
	return pluginsapi.Version{
		PluginID:    v.PluginID,
		Version:     v.Version,
		MinHostSDK:  v.MinHostSDK,
		SizeInBytes: v.SizeInBytes,
		SHA256:      v.SHA256,
		IsYanked:    v.IsYanked,
		PublishedAt: v.PublishedAt,
	}
}
