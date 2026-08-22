package domain_test

import (
	"strings"
	"testing"
	"time"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

var published = time.Date(2026, time.August, 1, 9, 0, 0, 0, time.UTC)

const digest = "9f2a4c1e5b6d7a8f0c3e2b1d4a5f6e7c8b9a0d1e2f3a4b5c6d7e8f9a0b1c2d3e"

func TestNewPluginRefusesAnIdentityThatIsNotLowerKebab(t *testing.T) {
	for _, id := range []string{"", "Weather", "weather editor", "weather--editor", "-weather", "weather-"} {
		if _, err := domain.NewPlugin(id, "Weather", "", "", published); errs.KindOf(err) != errs.Invalid {
			t.Errorf("NewPlugin(%q) kind = %v, want %v", id, errs.KindOf(err), errs.Invalid)
		}
	}
}

func TestNewPluginRefusesAMissingDisplayName(t *testing.T) {
	if _, err := domain.NewPlugin("weather", "  ", "", "", published); errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

func TestNewPluginMeasuresTextInRunesNotBytes(t *testing.T) {
	// Every rune here is three bytes, so a byte-counting limit would refuse a name well inside it.
	name := strings.Repeat("あ", domain.MaxDisplayNameLength)

	if _, err := domain.NewPlugin("weather", name, "", "", published); err != nil {
		t.Errorf("NewPlugin with %d runes = %v, want no error", domain.MaxDisplayNameLength, err)
	}

	tooLong := strings.Repeat("あ", domain.MaxDisplayNameLength+1)
	if _, err := domain.NewPlugin("weather", tooLong, "", "", published); errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

func TestNewPluginIsListedFromTheStart(t *testing.T) {
	p, err := domain.NewPlugin("weather", " Weather ", " Forecasts. ", " npham ", published)
	if err != nil {
		t.Fatalf("NewPlugin = %v", err)
	}

	if !p.IsListed {
		t.Error("IsListed = false, want true")
	}
	if p.DisplayName != "Weather" || p.Description != "Forecasts." || p.Publisher != "npham" {
		t.Errorf("surrounding space survived: %+v", p)
	}
}

func TestNewVersionRefusesAVersionThatIsNotMajorMinorPatch(t *testing.T) {
	for _, v := range []string{"", "1.0", "1.0.0.0", "1.0.0-beta", "v1.0.0"} {
		_, err := domain.NewVersion("weather", v, "1.1", digest, 10, 100, published)
		if errs.KindOf(err) != errs.Invalid {
			t.Errorf("NewVersion(%q) kind = %v, want %v", v, errs.KindOf(err), errs.Invalid)
		}
	}
}

func TestNewVersionRefusesAHostSdkThatIsNotMajorMinor(t *testing.T) {
	for _, sdk := range []string{"", "1", "1.2.3", "next"} {
		_, err := domain.NewVersion("weather", "1.0.0", sdk, digest, 10, 100, published)
		if errs.KindOf(err) != errs.Invalid {
			t.Errorf("NewVersion sdk %q kind = %v, want %v", sdk, errs.KindOf(err), errs.Invalid)
		}
	}
}

func TestNewVersionRefusesADigestThatIsNotASha256(t *testing.T) {
	for _, d := range []string{"", "abc", strings.Repeat("z", 64), strings.ToUpper(digest) + "0"} {
		_, err := domain.NewVersion("weather", "1.0.0", "1.1", d, 10, 100, published)
		if errs.KindOf(err) != errs.Invalid {
			t.Errorf("NewVersion digest %q kind = %v, want %v", d, errs.KindOf(err), errs.Invalid)
		}
	}
}

func TestNewVersionAcceptsAnUpperCaseDigestAndStoresItLowered(t *testing.T) {
	v, err := domain.NewVersion("weather", "1.0.0", "1.1", strings.ToUpper(digest), 10, 100, published)
	if err != nil {
		t.Fatalf("NewVersion = %v", err)
	}

	if v.SHA256 != digest {
		t.Errorf("SHA256 = %q, want %q", v.SHA256, digest)
	}
}

func TestNewVersionRefusesEmptyAndOversizedPackages(t *testing.T) {
	if _, err := domain.NewVersion("weather", "1.0.0", "1.1", digest, 0, 100, published); errs.KindOf(err) != errs.Invalid {
		t.Errorf("empty kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if _, err := domain.NewVersion("weather", "1.0.0", "1.1", digest, 101, 100, published); errs.KindOf(err) != errs.Invalid {
		t.Errorf("oversized kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

func TestValidatePluginIDAgreesWithNewPlugin(t *testing.T) {
	if err := domain.ValidatePluginID("weather-editor"); err != nil {
		t.Errorf("ValidatePluginID = %v, want no error", err)
	}
	if err := domain.ValidatePluginID("Weather"); errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}
