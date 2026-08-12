// Package notesapi is the notes module's public contract: other modules import this package and
// nothing else under internal/modules/notes/. Interfaces, plain data and events only — no SQL, no
// protobuf, no other module.
package notesapi

import (
	"context"
	"time"
)

// Note is deliberately separate from the domain entity in internal/modules/notes/domain. The two
// look almost identical today; the point is that the domain type is free to grow invariants,
// unexported fields and behaviour without any of it leaking across the module boundary.
type Note struct {
	ID        int64
	Title     string
	Body      string
	CreatedAt time.Time
	UpdatedAt time.Time
}

type Service interface {
	// List returns every note, most recently updated first.
	List(ctx context.Context) ([]Note, error)

	// Get fails with an errs.NotFound error when id does not exist.
	Get(ctx context.Context, id int64) (Note, error)

	// Create fails with an errs.Invalid error, whose message is safe to show a user, when the
	// title is empty or whitespace-only.
	Create(ctx context.Context, title, body string) (Note, error)

	Update(ctx context.Context, id int64, title, body string) (Note, error)

	// Delete of a note that is already gone succeeds: the caller wanted it gone, and it is.
	Delete(ctx context.Context, id int64) error
}

type Created struct {
	Note Note
}

type Updated struct {
	Note Note
}

// Deleted carries the title as well as the ID because by the time a subscriber runs the note is
// gone, and "Deleted 'Shopping list'" is a message you can only write if the event brought the name
// with it.
type Deleted struct {
	ID    int64
	Title string
}
