// Package service implements the notes use cases. It is private to the module.
package service

import (
	"context"
	"time"

	notesapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

// Store is the persistence this service needs, declared here rather than in store/.
//
// The interface belongs to the consumer, which is what lets these use cases be tested against a
// fake in microseconds. The desktop original could take the concrete store instead, because SQLite
// has an in-memory mode and a real database per test costs nothing. SQL Server has no such mode:
// without this seam, every service test would need a container, and tests that need a container
// are tests people stop running.
//
// The database is still tested — by the integration tests, against a real server. What is not
// worth doing is testing the orchestration and the SQL together, every time, on every machine.
type Store interface {
	List(ctx context.Context) ([]domain.Note, error)
	Get(ctx context.Context, id int64) (domain.Note, error)
	Insert(ctx context.Context, n domain.Note) (domain.Note, error)

	// Update takes a pointer because the store may adjust the note to match what it can actually
	// hold — timestamps are truncated to the column's precision — and the caller has to end up
	// with the values that were stored, not the ones it hoped for.
	Update(ctx context.Context, n *domain.Note) error

	Delete(ctx context.Context, id int64) error
}

// Clock hands the service the current time.
//
// It exists so tests can pin "now" instead of asserting on ranges. The production value is
// time.Now, and that is the only place the real clock is read.
type Clock func() time.Time

// Service is the notesapi.Service implementation.
type Service struct {
	store Store
	bus   *eventbus.Bus
	now   Clock
}

// New builds the service. Pass nil for clock to use the wall clock.
func New(store Store, bus *eventbus.Bus, clock Clock) *Service {
	if clock == nil {
		clock = time.Now
	}
	return &Service{store: store, bus: bus, now: clock}
}

// List returns every note, most recently updated first.
func (s *Service) List(ctx context.Context) ([]notesapi.Note, error) {
	notes, err := s.store.List(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]notesapi.Note, len(notes))
	for i, n := range notes {
		out[i] = toAPI(n)
	}
	return out, nil
}

// Get returns one note.
func (s *Service) Get(ctx context.Context, id int64) (notesapi.Note, error) {
	n, err := s.store.Get(ctx, id)
	if err != nil {
		return notesapi.Note{}, err
	}
	return toAPI(n), nil
}

// Create validates and stores a new note, then announces it.
func (s *Service) Create(ctx context.Context, title, body string) (notesapi.Note, error) {
	// The domain decides whether this is a legal note. The service's job is to orchestrate (ask
	// the domain, then the store), not to re-implement the rules.
	n, err := domain.NewNote(title, body, s.now())
	if err != nil {
		return notesapi.Note{}, err
	}

	stored, err := s.store.Insert(ctx, n)
	if err != nil {
		return notesapi.Note{}, err
	}

	out := toAPI(stored)
	// Published after the write, never before. An event is a statement that something happened,
	// and a create that failed did not happen.
	eventbus.Publish(s.bus, ctx, notesapi.Created{Note: out})
	return out, nil
}

// Update rewrites a note.
func (s *Service) Update(
	ctx context.Context, id int64, title, body string,
) (notesapi.Note, error) {
	// Read before write: the entity has to exist before its invariants can be re-checked, and
	// CreatedAt has to survive the round trip.
	n, err := s.store.Get(ctx, id)
	if err != nil {
		return notesapi.Note{}, err
	}

	now := s.now()
	if err := n.Rename(title, now); err != nil {
		return notesapi.Note{}, err
	}
	n.Rewrite(body, now)

	if err := s.store.Update(ctx, &n); err != nil {
		return notesapi.Note{}, err
	}

	out := toAPI(n)
	eventbus.Publish(s.bus, ctx, notesapi.Updated{Note: out})
	return out, nil
}

// Delete removes a note and announces it.
func (s *Service) Delete(ctx context.Context, id int64) error {
	// Fetch first, purely so the event can carry the title. Subscribers cannot look it up
	// afterwards. That is the whole problem with deletion events.
	n, err := s.store.Get(ctx, id)
	if errs.KindOf(err) == errs.NotFound {
		// Already gone is what the caller asked for. NotFound here would make a delete retried
		// after a dropped connection look failed. Nothing is published: this call removed nothing.
		return nil
	}
	if err != nil {
		return err
	}

	if err := s.store.Delete(ctx, id); err != nil {
		return err
	}

	eventbus.Publish(s.bus, ctx, notesapi.Deleted{ID: id, Title: n.Title})
	return nil
}

// toAPI maps the domain entity onto the DTO the rest of the application sees. It is the border
// checkpoint: nothing crosses out of the module without passing through here.
func toAPI(n domain.Note) notesapi.Note {
	return notesapi.Note{
		ID:        n.ID,
		Title:     n.Title,
		Body:      n.Body,
		CreatedAt: n.CreatedAt,
		UpdatedAt: n.UpdatedAt,
	}
}

var _ notesapi.Service = (*Service)(nil)
