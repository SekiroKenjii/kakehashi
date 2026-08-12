package service

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"testing"
	"time"

	notesapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

var (
	created = time.Date(2026, time.August, 1, 9, 0, 0, 0, time.UTC)
	edited  = time.Date(2026, time.August, 5, 14, 30, 0, 0, time.UTC)
)

type fakeStore struct {
	notes map[int64]domain.Note

	getErr    error
	insertErr error
	updateErr error
	deleteErr error

	inserted []domain.Note
	updated  []domain.Note
	deleted  []int64
	nextID   int64
}

func newFakeStore() *fakeStore {
	return &fakeStore{notes: make(map[int64]domain.Note), nextID: 1}
}

func (f *fakeStore) seed(n domain.Note) domain.Note {
	if n.ID == 0 {
		n.ID = f.nextID
		f.nextID++
	}
	f.notes[n.ID] = n
	return n
}

func (f *fakeStore) List(context.Context) ([]domain.Note, error) {
	out := make([]domain.Note, 0, len(f.notes))
	for _, n := range f.notes {
		out = append(out, n)
	}
	return out, nil
}

func (f *fakeStore) Get(_ context.Context, id int64) (domain.Note, error) {
	if f.getErr != nil {
		return domain.Note{}, f.getErr
	}
	n, ok := f.notes[id]
	if !ok {
		return domain.Note{}, errs.NotFoundf("No note with ID %d.", id)
	}
	return n, nil
}

func (f *fakeStore) Insert(_ context.Context, n domain.Note) (domain.Note, error) {
	if f.insertErr != nil {
		return domain.Note{}, f.insertErr
	}
	f.inserted = append(f.inserted, n)
	n.ID = f.nextID
	f.nextID++
	f.notes[n.ID] = n
	return n, nil
}

func (f *fakeStore) Update(_ context.Context, n *domain.Note) error {
	if f.updateErr != nil {
		return f.updateErr
	}
	f.updated = append(f.updated, *n)
	f.notes[n.ID] = *n
	return nil
}

func (f *fakeStore) Delete(_ context.Context, id int64) error {
	if f.deleteErr != nil {
		return f.deleteErr
	}
	f.deleted = append(f.deleted, id)
	delete(f.notes, id)
	return nil
}

type recorder struct {
	created []notesapi.Created
	updated []notesapi.Updated
	deleted []notesapi.Deleted
}

func newBus(t *testing.T) (*eventbus.Bus, *recorder) {
	t.Helper()

	bus := eventbus.New(slog.New(slog.NewTextHandler(io.Discard, nil)))
	rec := &recorder{}

	eventbus.Subscribe(bus, func(_ context.Context, e notesapi.Created) {
		rec.created = append(rec.created, e)
	})
	eventbus.Subscribe(bus, func(_ context.Context, e notesapi.Updated) {
		rec.updated = append(rec.updated, e)
	})
	eventbus.Subscribe(bus, func(_ context.Context, e notesapi.Deleted) {
		rec.deleted = append(rec.deleted, e)
	})

	return bus, rec
}

func pinned(at time.Time) Clock { return func() time.Time { return at } }

func TestCreateStoresAndAnnounces(t *testing.T) {
	store := newFakeStore()
	bus, rec := newBus(t)

	note, err := New(store, bus, pinned(created)).Create(context.Background(), " Groceries ", "milk")
	if err != nil {
		t.Fatalf("Create returned an error: %v", err)
	}

	if note.ID == 0 {
		t.Error("returned note has no ID; the store's assigned ID was dropped")
	}
	if note.Title != "Groceries" {
		t.Errorf("Title = %q, want it trimmed to %q", note.Title, "Groceries")
	}
	if len(store.inserted) != 1 {
		t.Fatalf("store saw %d inserts, want 1", len(store.inserted))
	}
	if len(rec.created) != 1 || rec.created[0].Note.ID != note.ID {
		t.Errorf("published %+v, want one Created carrying ID %d", rec.created, note.ID)
	}
}

func TestCreateRejectsABlankTitleWithoutTouchingTheStore(t *testing.T) {
	store := newFakeStore()
	bus, rec := newBus(t)

	_, err := New(store, bus, pinned(created)).Create(context.Background(), "   ", "body")

	if err == nil {
		t.Fatal("Create with a blank title succeeded, want a failure")
	}
	if got := errs.KindOf(err); got != errs.Invalid {
		t.Errorf("kind = %v, want %v", got, errs.Invalid)
	}
	if len(store.inserted) != 0 {
		t.Errorf("store saw %d inserts, want none", len(store.inserted))
	}
	if len(rec.created) != 0 {
		t.Errorf("published %d Created events for a create that failed", len(rec.created))
	}
}

func TestCreateAnnouncesNothingWhenTheStoreFails(t *testing.T) {
	store := newFakeStore()
	store.insertErr = errors.New("connection reset")
	bus, rec := newBus(t)

	_, err := New(store, bus, pinned(created)).Create(context.Background(), "Title", "")

	if err == nil {
		t.Fatal("Create succeeded despite the store failing")
	}
	if len(rec.created) != 0 {
		t.Errorf("published %d Created events for a write that failed", len(rec.created))
	}
}

func TestUpdatePreservesCreatedAtAndAnnounces(t *testing.T) {
	store := newFakeStore()
	existing := store.seed(domain.Note{
		Title: "Before", Body: "old", CreatedAt: created, UpdatedAt: created,
	})
	bus, rec := newBus(t)

	note, err := New(store, bus, pinned(edited)).
		Update(context.Background(), existing.ID, "After", "new")
	if err != nil {
		t.Fatalf("Update returned an error: %v", err)
	}

	if note.Title != "After" || note.Body != "new" {
		t.Errorf("note = %q/%q, want After/new", note.Title, note.Body)
	}
	if !note.CreatedAt.Equal(created) {
		t.Errorf("CreatedAt = %v, want it to survive the round trip at %v", note.CreatedAt, created)
	}
	if !note.UpdatedAt.Equal(edited) {
		t.Errorf("UpdatedAt = %v, want %v", note.UpdatedAt, edited)
	}
	if len(rec.updated) != 1 {
		t.Errorf("published %d Updated events, want 1", len(rec.updated))
	}
}

func TestUpdateRejectsABlankTitleWithoutSaving(t *testing.T) {
	store := newFakeStore()
	existing := store.seed(domain.Note{
		Title: "Original", CreatedAt: created, UpdatedAt: created,
	})
	bus, rec := newBus(t)

	_, err := New(store, bus, pinned(edited)).Update(context.Background(), existing.ID, "", "body")

	if err == nil {
		t.Fatal("Update with a blank title succeeded, want a failure")
	}
	if len(store.updated) != 0 {
		t.Errorf("store saw %d updates, want none", len(store.updated))
	}
	if len(rec.updated) != 0 {
		t.Errorf("published %d Updated events for an update that failed", len(rec.updated))
	}
}

func TestUpdateReportsAMissingNote(t *testing.T) {
	bus, _ := newBus(t)

	_, err := New(newFakeStore(), bus, pinned(edited)).
		Update(context.Background(), 404, "Title", "body")

	if got := errs.KindOf(err); got != errs.NotFound {
		t.Errorf("kind = %v, want %v", got, errs.NotFound)
	}
}

func TestDeleteCarriesTheTitleIntoTheEvent(t *testing.T) {
	store := newFakeStore()
	existing := store.seed(domain.Note{
		Title: "Shopping list", CreatedAt: created, UpdatedAt: created,
	})
	bus, rec := newBus(t)

	if err := New(store, bus, nil).Delete(context.Background(), existing.ID); err != nil {
		t.Fatalf("Delete returned an error: %v", err)
	}

	if len(rec.deleted) != 1 {
		t.Fatalf("published %d Deleted events, want 1", len(rec.deleted))
	}
	// Why Delete reads before it writes: a subscriber cannot look up a note that is already gone,
	// so the event has to bring the name with it.
	if rec.deleted[0].Title != "Shopping list" {
		t.Errorf("Deleted.Title = %q, want %q", rec.deleted[0].Title, "Shopping list")
	}
	if rec.deleted[0].ID != existing.ID {
		t.Errorf("Deleted.ID = %d, want %d", rec.deleted[0].ID, existing.ID)
	}
}

func TestDeleteIsIdempotent(t *testing.T) {
	store := newFakeStore()
	bus, rec := newBus(t)

	err := New(store, bus, nil).Delete(context.Background(), 404)

	if err != nil {
		t.Fatalf("deleting a missing note returned %v, want success", err)
	}
	if len(store.deleted) != 0 {
		t.Errorf("store saw %d deletes, want none", len(store.deleted))
	}
	if len(rec.deleted) != 0 {
		t.Errorf("published %d Deleted events for a note that was never there", len(rec.deleted))
	}
}

func TestDeleteReportsAStoreFailure(t *testing.T) {
	// Idempotence covers "not found"; it must not swallow a database that is actually broken.
	store := newFakeStore()
	existing := store.seed(domain.Note{Title: "Doomed", CreatedAt: created, UpdatedAt: created})
	store.deleteErr = errors.New("connection reset")
	bus, rec := newBus(t)

	err := New(store, bus, nil).Delete(context.Background(), existing.ID)

	if err == nil {
		t.Fatal("Delete succeeded despite the store failing")
	}
	if len(rec.deleted) != 0 {
		t.Errorf("published %d Deleted events for a delete that failed", len(rec.deleted))
	}
}

func TestListMapsEveryNoteToTheContract(t *testing.T) {
	store := newFakeStore()
	store.seed(domain.Note{Title: "One", CreatedAt: created, UpdatedAt: created})
	store.seed(domain.Note{Title: "Two", CreatedAt: created, UpdatedAt: edited})
	bus, _ := newBus(t)

	notes, err := New(store, bus, nil).List(context.Background())
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if len(notes) != 2 {
		t.Fatalf("got %d notes, want 2", len(notes))
	}
	for _, n := range notes {
		if n.ID == 0 || n.Title == "" {
			t.Errorf("note %+v came back unmapped", n)
		}
	}
}

func TestNewDefaultsToTheWallClock(t *testing.T) {
	store := newFakeStore()
	bus, _ := newBus(t)
	before := time.Now()

	note, err := New(store, bus, nil).Create(context.Background(), "Title", "")
	if err != nil {
		t.Fatalf("Create returned an error: %v", err)
	}

	if note.CreatedAt.Before(before) {
		t.Errorf("CreatedAt = %v, want at or after %v", note.CreatedAt, before)
	}
}
