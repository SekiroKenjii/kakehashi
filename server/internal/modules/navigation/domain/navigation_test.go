package domain_test

import (
	"strings"
	"testing"

	"__GO_MODULE__/server/internal/modules/navigation/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

func TestSlugMakesAStableIdentifierOutOfATitle(t *testing.T) {
	cases := map[string]string{
		"Utilities":        "utilities",
		"Ops / Tools":      "ops-tools",
		"  Monitoring  ":   "monitoring",
		"DevOps & SRE":     "devops-sre",
		"Reports 2026":     "reports-2026",
		"----":             "",
		"Người dùng":       "ng-i-d-ng",
		"Multiple   Words": "multiple-words",
	}

	for title, want := range cases {
		if got := domain.Slug(title); got != want {
			t.Errorf("Slug(%q) = %q, want %q", title, got, want)
		}
	}
}

// A slug ends up in a URL, a log line and a configuration file. Anything that would arrive at those
// three places spelled differently is refused rather than mangled.
func TestValidateSlugRefusesAnythingThatWouldNotSurviveBeingAnIdentifier(t *testing.T) {
	for _, id := range []string{"", "Utilities", "ops tools", "ops/tools", "ops_tools", "ops.tools"} {
		if err := domain.ValidateSlug(id); errs.KindOf(err) != errs.Invalid {
			t.Errorf("ValidateSlug(%q) = %v, want an invalid-argument", id, err)
		}
	}
	for _, id := range []string{"utilities", "ops-tools", "reports-2026", "a"} {
		if err := domain.ValidateSlug(id); err != nil {
			t.Errorf("ValidateSlug(%q) = %v, want it accepted", id, err)
		}
	}
}

func TestNewGroupDerivesTheIdentifierWhenNoneIsGiven(t *testing.T) {
	group, err := domain.NewGroup("", "  Monitoring  ", 30, false)
	if err != nil {
		t.Fatalf("NewGroup: %v", err)
	}
	if group.ID != "monitoring" {
		t.Errorf("id is %q, want monitoring derived from the title", group.ID)
	}
	if group.Title != "Monitoring" {
		t.Errorf("title is %q, want it trimmed", group.Title)
	}
}

func TestNewGroupRefusesAHeadingNobodyCouldRead(t *testing.T) {
	if _, err := domain.NewGroup("", "   ", 0, false); errs.KindOf(err) != errs.Invalid {
		t.Errorf("a blank title returned %v, want an invalid-argument", err)
	}
	long := strings.Repeat("x", domain.MaxTitle+1)
	if _, err := domain.NewGroup("", long, 0, false); errs.KindOf(err) != errs.Invalid {
		t.Errorf("an over-long title returned %v, want an invalid-argument", err)
	}
	// A title that slugifies to nothing cannot be stored under an identifier at all.
	if _, err := domain.NewGroup("", "///", 0, false); errs.KindOf(err) != errs.Invalid {
		t.Errorf("a title with no usable characters returned %v, want an invalid-argument", err)
	}
}

// Empty is a legal override and means "clear it". There has to be a way back to what the code calls a
// page, or renaming one is permanent.
func TestNormaliseOverrideAcceptsEmptyAndRefusesTooLong(t *testing.T) {
	if got, err := domain.NormaliseOverride("navigation label", "  "); err != nil || got != "" {
		t.Errorf("NormaliseOverride(blank) = %q, %v; want an accepted empty string", got, err)
	}

	long := strings.Repeat("x", domain.MaxTitle+1)
	if _, err := domain.NormaliseOverride("navigation label", long); errs.KindOf(err) != errs.Invalid {
		t.Errorf("an over-long override returned %v, want an invalid-argument", err)
	}
}
