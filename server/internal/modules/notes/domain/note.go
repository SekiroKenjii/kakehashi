// Package domain holds the notes module's entities and the rules they enforce.
//
// It is the innermost layer: it imports the platform's error types and nothing else. No SQL, no
// protobuf, no other module. That is what makes the rules in here testable without standing up a
// database or a server.
package domain

import (
	"strings"
	"time"

	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/text"
)

// MaxTitleLength caps a note's title.
//
// The limit is about the interface, not the storage: a title is a list row, and a row that runs to
// three lines stops being a label and starts being the note itself. The column is sized to match,
// so a title that passes here always fits.
const MaxTitleLength = 120

// Note is the entity.
//
// Its fields are exported for the store to scan into, but construction goes through NewNote, which
// is where the invariants live. A zero Note is not a valid one.
type Note struct {
	ID        int64
	Title     string
	Body      string
	CreatedAt time.Time
	UpdatedAt time.Time
}

// NewNote builds a valid note, or explains why it cannot.
//
// now is passed in rather than read from the clock so tests can pin it. Reaching for time.Now
// inside the domain is the fastest way to end up with a test that passes everywhere except at
// midnight.
func NewNote(title, body string, now time.Time) (Note, error) {
	title, err := normalizeTitle(title)
	if err != nil {
		return Note{}, err
	}

	return Note{
		Title:     title,
		Body:      body,
		CreatedAt: now,
		UpdatedAt: now,
	}, nil
}

// Rename changes the title, keeping the invariants.
func (n *Note) Rename(title string, now time.Time) error {
	title, err := normalizeTitle(title)
	if err != nil {
		return err
	}
	n.Title = title
	n.UpdatedAt = now
	return nil
}

// Rewrite replaces the body.
func (n *Note) Rewrite(body string, now time.Time) {
	n.Body = body
	n.UpdatedAt = now
}

func normalizeTitle(title string) (string, error) {
	title = strings.TrimSpace(title)

	if title == "" {
		return "", errs.Invalidf("A note needs a title.")
	}
	// Runes, not bytes: len() lets a Vietnamese title through at 40 characters and rejects an
	// English one at 121.
	if text.UTF16Len(title) > MaxTitleLength {
		return "", errs.Invalidf("Titles are limited to %d characters.", MaxTitleLength)
	}
	return title, nil
}
