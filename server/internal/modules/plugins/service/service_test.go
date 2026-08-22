package service

import (
	"context"
	"io"
	"log/slog"
	"time"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/eventbus"
)

var now = time.Date(2026, time.August, 1, 9, 0, 0, 0, time.UTC)

// fakeStore records what the service asked it to do and answers with whatever the test set up.
type fakeStore struct {
	plugins  map[string]domain.Plugin
	versions map[string]domain.Version
	content  map[string][]byte

	inserted []domain.Version
	installs []install
	yanked   []string
	listed   []string
}

type install struct {
	userID   string
	pluginID string
	version  string
	source   string
}

func newFakeStore() *fakeStore {
	return &fakeStore{
		plugins:  make(map[string]domain.Plugin),
		versions: make(map[string]domain.Version),
		content:  make(map[string][]byte),
	}
}

func key(pluginID, version string) string { return pluginID + "@" + version }

func (f *fakeStore) seedPlugin(p domain.Plugin) { f.plugins[p.PluginID] = p }

func (f *fakeStore) seedVersion(v domain.Version, content []byte) {
	f.versions[key(v.PluginID, v.Version)] = v
	f.content[key(v.PluginID, v.Version)] = content
}

func (f *fakeStore) ListListed(context.Context) ([]domain.Plugin, error) {
	var out []domain.Plugin
	for _, p := range f.plugins {
		if p.IsListed {
			out = append(out, p)
		}
	}
	return out, nil
}

func (f *fakeStore) GetPlugin(_ context.Context, pluginID string) (domain.Plugin, error) {
	p, ok := f.plugins[pluginID]
	if !ok {
		return domain.Plugin{}, errs.NotFoundf("No plugin called %q.", pluginID)
	}
	return p, nil
}

func (f *fakeStore) UpsertPlugin(_ context.Context, p domain.Plugin) error {
	f.plugins[p.PluginID] = p
	return nil
}

func (f *fakeStore) SetListed(_ context.Context, pluginID string, listed bool, _ time.Time) error {
	p, ok := f.plugins[pluginID]
	if !ok {
		return errs.NotFoundf("No plugin called %q.", pluginID)
	}
	p.IsListed = listed
	f.plugins[pluginID] = p
	f.listed = append(f.listed, pluginID)
	return nil
}

func (f *fakeStore) LatestVersions(context.Context) (map[string]domain.Version, error) {
	latest := make(map[string]domain.Version)
	for _, v := range f.versions {
		if v.IsYanked {
			continue
		}
		if best, ok := latest[v.PluginID]; !ok || v.PublishedAt.After(best.PublishedAt) {
			latest[v.PluginID] = v
		}
	}
	return latest, nil
}

func (f *fakeStore) ListVersions(_ context.Context, pluginID string) ([]domain.Version, error) {
	var out []domain.Version
	for _, v := range f.versions {
		if v.PluginID == pluginID {
			out = append(out, v)
		}
	}
	return out, nil
}

func (f *fakeStore) GetVersion(_ context.Context, pluginID, version string) (domain.Version, error) {
	v, ok := f.versions[key(pluginID, version)]
	if !ok {
		return domain.Version{}, errs.NotFoundf("No version %s of %q.", version, pluginID)
	}
	return v, nil
}

func (f *fakeStore) InsertVersion(_ context.Context, v domain.Version, content []byte) error {
	if _, exists := f.versions[key(v.PluginID, v.Version)]; exists {
		return errs.Invalidf("Version %s of %q is already published.", v.Version, v.PluginID)
	}
	f.seedVersion(v, content)
	f.inserted = append(f.inserted, v)
	return nil
}

func (f *fakeStore) SetYanked(_ context.Context, pluginID, version string, yanked bool) error {
	v, ok := f.versions[key(pluginID, version)]
	if !ok {
		return errs.NotFoundf("No version %s of %q.", version, pluginID)
	}
	v.IsYanked = yanked
	f.versions[key(pluginID, version)] = v
	f.yanked = append(f.yanked, key(pluginID, version))
	return nil
}

func (f *fakeStore) WriteContent(_ context.Context, pluginID, version string, w io.Writer) error {
	content, ok := f.content[key(pluginID, version)]
	if !ok {
		return errs.NotFoundf("No version %s of %q.", version, pluginID)
	}
	_, err := w.Write(content)
	return err
}

func (f *fakeStore) InsertInstall(
	_ context.Context, userID, pluginID, version, source string, _ time.Time,
) error {
	f.installs = append(f.installs, install{userID, pluginID, version, source})
	return nil
}

func newService(store Store) *Service {
	bus := eventbus.New(slog.New(slog.NewTextHandler(io.Discard, nil)))
	return New(store, bus, func() time.Time { return now })
}

var _ Store = (*fakeStore)(nil)
