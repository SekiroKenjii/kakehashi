package service

import (
	"bytes"
	"context"
	"testing"

	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

func seedVersion(store *fakeStore) domain.Version {
	v := domain.Version{
		PluginID:    "weather",
		Version:     "1.0.0",
		MinHostSDK:  "1.1",
		SizeInBytes: 9,
		SHA256:      digestOf([]byte("a package")),
		PublishedAt: now,
	}
	store.seedVersion(v, []byte("a package"))
	return v
}

func TestRecordInstallStoresWhatTheTokenSaid(t *testing.T) {
	store := newFakeStore()
	seedVersion(store)

	err := newService(store).RecordInstall(
		context.Background(), "user-1", "weather", "1.0.0", pluginsapi.SourceCatalog)
	if err != nil {
		t.Fatalf("RecordInstall = %v", err)
	}

	if len(store.installs) != 1 {
		t.Fatalf("recorded %d installs, want 1", len(store.installs))
	}
	if got := store.installs[0]; got.userID != "user-1" || got.source != pluginsapi.SourceCatalog {
		t.Errorf("install = %+v", got)
	}
}

func TestRecordInstallRefusesASourceOutsideTheClosedSet(t *testing.T) {
	store := newFakeStore()
	seedVersion(store)

	for _, source := range []string{"", "Trusted", "catalog", "Official"} {
		err := newService(store).RecordInstall(
			context.Background(), "user-1", "weather", "1.0.0", source)

		if errs.KindOf(err) != errs.Invalid {
			t.Errorf("source %q kind = %v, want %v", source, errs.KindOf(err), errs.Invalid)
		}
	}
	if len(store.installs) != 0 {
		t.Error("an install with an invented source was recorded")
	}
}

// The refusal must not enumerate what is allowed: naming the set teaches a caller what else to try.
func TestRecordInstallRefusalNamesNoSources(t *testing.T) {
	store := newFakeStore()
	seedVersion(store)

	err := newService(store).RecordInstall(context.Background(), "user-1", "weather", "1.0.0", "Trusted")

	for _, source := range []string{pluginsapi.SourceCatalog, pluginsapi.SourceURL, pluginsapi.SourceFile} {
		if bytes.Contains([]byte(err.Error()), []byte(source)) {
			t.Errorf("the refusal names %q", source)
		}
	}
}

func TestRecordInstallRefusesAVersionThisCatalogDoesNotHold(t *testing.T) {
	store := newFakeStore()

	err := newService(store).RecordInstall(
		context.Background(), "user-1", "weather", "9.9.9", pluginsapi.SourceFile)

	if errs.KindOf(err) != errs.NotFound {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.NotFound)
	}
}

func TestRecordInstallNeedsAnAccount(t *testing.T) {
	store := newFakeStore()
	seedVersion(store)

	err := newService(store).RecordInstall(
		context.Background(), "", "weather", "1.0.0", pluginsapi.SourceCatalog)

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

func TestDownloadWritesTheArtifactEvenWhenYanked(t *testing.T) {
	store := newFakeStore()
	v := seedVersion(store)
	v.IsYanked = true
	store.seedVersion(v, []byte("a package"))

	var out bytes.Buffer
	if err := newService(store).Download(context.Background(), "weather", "1.0.0", &out); err != nil {
		t.Fatalf("Download = %v", err)
	}

	if out.String() != "a package" {
		t.Errorf("wrote %q", out.String())
	}
}

func TestListLeavesOutAPluginWithNothingOnOffer(t *testing.T) {
	store := newFakeStore()
	plugin, err := domain.NewPlugin("weather", "Weather", "", "", now)
	if err != nil {
		t.Fatalf("NewPlugin = %v", err)
	}
	store.seedPlugin(plugin)
	v := seedVersion(store)
	v.IsYanked = true
	store.seedVersion(v, []byte("a package"))

	listings, err := newService(store).List(context.Background())
	if err != nil {
		t.Fatalf("List = %v", err)
	}

	if len(listings) != 0 {
		t.Errorf("listed %d plugins, want none: every version is withdrawn", len(listings))
	}
}
