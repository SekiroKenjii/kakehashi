package domain

import (
	"strings"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

var (
	created = time.Date(2026, time.August, 1, 9, 0, 0, 0, time.UTC)
	edited  = time.Date(2026, time.August, 5, 14, 30, 0, 0, time.UTC)
)

func TestNewNoteTrimsTheTitle(t *testing.T) {
	n, err := NewNote("  Shopping list  ", "milk", created)
	if err != nil {
		t.Fatalf("NewNote returned an error: %v", err)
	}

	if n.Title != "Shopping list" {
		t.Errorf("Title = %q, want %q", n.Title, "Shopping list")
	}
	if n.Body != "milk" {
		t.Errorf("Body = %q, want %q", n.Body, "milk")
	}
	if !n.CreatedAt.Equal(created) || !n.UpdatedAt.Equal(created) {
		t.Errorf("timestamps = %v/%v, want both %v", n.CreatedAt, n.UpdatedAt, created)
	}
}

func TestNewNoteRejectsATitleThatIsOnlySpace(t *testing.T) {
	for _, title := range []string{"", "   ", "\t\n"} {
		_, err := NewNote(title, "body", created)

		if err == nil {
			t.Fatalf("NewNote(%q) succeeded, want a failure", title)
		}
		if got := errs.KindOf(err); got != errs.Invalid {
			t.Errorf("NewNote(%q) kind = %v, want %v", title, got, errs.Invalid)
		}
	}
}

func TestNewNoteAllowsAnEmptyBody(t *testing.T) {
	// A note is a title with optional contents, not the other way round. Requiring a body would
	// make "remember to call the bank" impossible to write.
	if _, err := NewNote("Title", "", created); err != nil {
		t.Fatalf("NewNote with an empty body returned an error: %v", err)
	}
}

func TestTitleLengthIsCountedInRunesNotBytes(t *testing.T) {
	// Every one of these is 3 bytes in UTF-8. Counting bytes would reject a Vietnamese title at 40
	// characters while letting an English one run to 120 — a rule that only looks correct in the
	// language it was written in.
	atLimit := strings.Repeat("ế", MaxTitleLength)
	overLimit := strings.Repeat("ế", MaxTitleLength+1)

	if _, err := NewNote(atLimit, "", created); err != nil {
		t.Errorf("a title of exactly %d runes was rejected: %v", MaxTitleLength, err)
	}

	_, err := NewNote(overLimit, "", created)
	if err == nil {
		t.Fatalf("a title of %d runes was accepted", MaxTitleLength+1)
	}
	if got := errs.KindOf(err); got != errs.Invalid {
		t.Errorf("kind = %v, want %v", got, errs.Invalid)
	}
}

func TestRenameKeepsCreatedAtAndMovesUpdatedAt(t *testing.T) {
	n, err := NewNote("Before", "body", created)
	if err != nil {
		t.Fatalf("NewNote returned an error: %v", err)
	}

	if err := n.Rename("  After  ", edited); err != nil {
		t.Fatalf("Rename returned an error: %v", err)
	}

	if n.Title != "After" {
		t.Errorf("Title = %q, want %q", n.Title, "After")
	}
	if !n.CreatedAt.Equal(created) {
		t.Errorf("CreatedAt = %v, want it unchanged at %v", n.CreatedAt, created)
	}
	if !n.UpdatedAt.Equal(edited) {
		t.Errorf("UpdatedAt = %v, want %v", n.UpdatedAt, edited)
	}
}

func TestRenameLeavesTheNoteUntouchedWhenItFails(t *testing.T) {
	// A rejected change must not half-apply. Setting the title before validating it is the classic
	// way to end up with an entity that violates its own invariant.
	n, err := NewNote("Original", "body", created)
	if err != nil {
		t.Fatalf("NewNote returned an error: %v", err)
	}

	if err := n.Rename("   ", edited); err == nil {
		t.Fatal("Rename with a blank title succeeded, want a failure")
	}

	if n.Title != "Original" {
		t.Errorf("Title = %q, want it unchanged at %q", n.Title, "Original")
	}
	if !n.UpdatedAt.Equal(created) {
		t.Errorf("UpdatedAt = %v, want it unchanged at %v", n.UpdatedAt, created)
	}
}

func TestRewriteReplacesTheBody(t *testing.T) {
	n, err := NewNote("Title", "old", created)
	if err != nil {
		t.Fatalf("NewNote returned an error: %v", err)
	}

	n.Rewrite("new", edited)

	if n.Body != "new" {
		t.Errorf("Body = %q, want %q", n.Body, "new")
	}
	if !n.UpdatedAt.Equal(edited) {
		t.Errorf("UpdatedAt = %v, want %v", n.UpdatedAt, edited)
	}
}
