package activityapi

import "testing"

// A kind that fell through would be invisible behind every chip while still being counted in the
// total. The list is written out rather than derived because Go cannot enumerate constants.
func TestEveryKindHasACategory(t *testing.T) {
	cases := []struct {
		kind string
		want string
	}{
		{KindSignedIn, CategorySignIn},
		{KindSignedOut, CategorySignIn},
		{KindNewDeviceSignedIn, CategorySignIn},
		{KindSessionRevoked, CategorySignIn},
		{KindSessionRevokedByAdmin, CategorySecurity},
		{KindFailedSignIn, CategorySecurity},
		{KindPasswordChanged, CategorySecurity},
		{KindAppUpdated, CategorySystem},
		{KindThemeChanged, CategorySystem},
	}

	for _, c := range cases {
		t.Run(c.kind, func(t *testing.T) {
			if got := CategoryOf(c.kind); got != c.want {
				t.Errorf("CategoryOf(%q) = %q, want %q", c.kind, got, c.want)
			}
		})
	}
}

// A kind from a newer build, or a wrong one, is worth a person's attention rather than a bucket
// nobody filters by.
func TestAnUnrecognisedKindIsShownRatherThanHidden(t *testing.T) {
	if got := CategoryOf("SomethingALaterBuildAdded"); got != CategorySecurity {
		t.Errorf("CategoryOf(unknown) = %q, want %q", got, CategorySecurity)
	}
	if got := CategoryOf(""); got != CategorySecurity {
		t.Errorf("CategoryOf(empty) = %q, want %q", got, CategorySecurity)
	}
}

// The allow-list is a security boundary, so what it does *not* contain is the assertion worth
// having: a kind added without this test failing is a write path somebody widened by accident.
func TestOnlyTheTwoClientFactsMayBeReported(t *testing.T) {
	want := []string{KindAppUpdated, KindThemeChanged}

	got := ClientReportableKinds()
	if len(got) != len(want) {
		t.Fatalf("reportable kinds = %v, want exactly %v", got, want)
	}
	for i, kind := range want {
		if got[i] != kind {
			t.Errorf("reportable[%d] = %q, want %q", i, got[i], kind)
		}
	}

	// Every other kind is the server's to write, not the client's.
	for kind := range categories {
		reportable := kind == KindAppUpdated || kind == KindThemeChanged
		if CanReport(kind) != reportable {
			t.Errorf("CanReport(%q) = %v, want %v", kind, CanReport(kind), reportable)
		}
	}
	if CanReport("SomethingElse") {
		t.Error("CanReport accepted a kind nobody defined")
	}
}
