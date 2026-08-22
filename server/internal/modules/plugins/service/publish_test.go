package service

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"testing"

	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

func digestOf(content []byte) string {
	sum := sha256.Sum256(content)
	return hex.EncodeToString(sum[:])
}

func publishArgs(content []byte, sha string) (pluginsapi.Plugin, pluginsapi.Version) {
	return pluginsapi.Plugin{
			PluginID:    "weather",
			DisplayName: "Weather",
			Description: "Forecast tiles.",
			Publisher:   "npham",
		}, pluginsapi.Version{
			PluginID:   "weather",
			Version:    "1.0.0",
			MinHostSDK: "1.1",
			SHA256:     sha,
		}
}

func TestPublishStoresThePackageAndItsPlugin(t *testing.T) {
	store := newFakeStore()
	content := []byte("a package")
	plugin, version := publishArgs(content, digestOf(content))

	out, err := newService(store).Publish(context.Background(), plugin, version, content)
	if err != nil {
		t.Fatalf("Publish = %v", err)
	}

	if out.SizeInBytes != int64(len(content)) {
		t.Errorf("SizeInBytes = %d, want %d", out.SizeInBytes, len(content))
	}
	if len(store.inserted) != 1 {
		t.Fatalf("inserted %d versions, want 1", len(store.inserted))
	}
	if _, ok := store.plugins["weather"]; !ok {
		t.Error("the plugin row was not written")
	}
}

func TestPublishRefusesADigestThatDisagreesWithTheBytes(t *testing.T) {
	store := newFakeStore()
	content := []byte("a package")
	// A well-formed digest of something else: the shape is right and the value is wrong, which is
	// exactly what a corrupted or swapped upload looks like.
	plugin, version := publishArgs(content, digestOf([]byte("a different package")))

	_, err := newService(store).Publish(context.Background(), plugin, version, content)

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if len(store.inserted) != 0 {
		t.Error("a package with the wrong digest was stored")
	}
}

func TestPublishRefusesAPackageOverTheLimit(t *testing.T) {
	store := newFakeStore()
	content := bytes.Repeat([]byte{0x42}, pluginsapi.MaxPackageBytes+1)
	plugin, version := publishArgs(content, digestOf(content))

	_, err := newService(store).Publish(context.Background(), plugin, version, content)

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

func TestPublishRefusesAVersionBelongingToAnotherPlugin(t *testing.T) {
	store := newFakeStore()
	content := []byte("a package")
	plugin, version := publishArgs(content, digestOf(content))
	version.PluginID = "something-else"

	_, err := newService(store).Publish(context.Background(), plugin, version, content)

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

func TestPublishRepublishingKeepsTheDescriptionCurrent(t *testing.T) {
	store := newFakeStore()
	existing, err := domain.NewPlugin("weather", "Weather", "Old words.", "npham", now)
	if err != nil {
		t.Fatalf("NewPlugin = %v", err)
	}
	store.seedPlugin(existing)

	content := []byte("a package")
	plugin, version := publishArgs(content, digestOf(content))

	if _, err := newService(store).Publish(context.Background(), plugin, version, content); err != nil {
		t.Fatalf("Publish = %v", err)
	}

	if got := store.plugins["weather"].Description; got != "Forecast tiles." {
		t.Errorf("Description = %q, want the one that was just published", got)
	}
}

func TestSetListedAndSetYankedRefuseAMalformedIdentity(t *testing.T) {
	svc := newService(newFakeStore())

	if err := svc.SetListed(context.Background(), "Weather", false); errs.KindOf(err) != errs.Invalid {
		t.Errorf("SetListed kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if err := svc.SetYanked(context.Background(), "Weather", "1.0.0", true); errs.KindOf(err) != errs.Invalid {
		t.Errorf("SetYanked kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}
