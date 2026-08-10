package domain

import (
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

var occurred = time.Date(2026, time.August, 6, 9, 30, 0, 0, time.UTC)

func TestNewEntryKeepsEveryFieldItWasGiven(t *testing.T) {
	entry, err := NewEntry("id-1", "account-1", "SignedIn", "laptop", "10.0.0.1", occurred)
	if err != nil {
		t.Fatalf("NewEntry returned an error: %v", err)
	}

	want := Entry{
		ID:         "id-1",
		UserID:     "account-1",
		Kind:       "SignedIn",
		Device:     "laptop",
		IPAddress:  "10.0.0.1",
		OccurredAt: occurred,
	}
	if entry != want {
		t.Errorf("entry = %+v, want %+v", entry, want)
	}
}

func TestNewEntryRejectsWhatWouldProduceAnUnusableRow(t *testing.T) {
	cases := []struct {
		name       string
		userID     string
		kind       string
		occurredAt time.Time
	}{
		{"no account", "", "SignedIn", occurred},
		{"no kind", "account-1", "", occurred},
		{"no time", "account-1", "SignedIn", time.Time{}},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, err := NewEntry("id-1", c.userID, c.kind, "laptop", "10.0.0.1", c.occurredAt)
			if err == nil {
				t.Fatal("NewEntry accepted it")
			}
			if errs.KindOf(err) != errs.Invalid {
				t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
			}
		})
	}
}

// Device and IP are the two fields that may legitimately be empty: an event published without a
// request behind it has neither, and a row that says only "Password changed" is still worth having.
func TestNewEntryAcceptsAnEntryWithNoDeviceOrAddress(t *testing.T) {
	if _, err := NewEntry("id-1", "account-1", "PasswordChanged", "", "", occurred); err != nil {
		t.Errorf("NewEntry returned an error: %v", err)
	}
}
