package service

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

var occurred = time.Date(2026, time.August, 6, 9, 30, 0, 0, time.UTC)

type fakeStore struct {
	inserted []domain.Entry
	feed     []domain.Entry

	// lastTake records what List was actually asked for, which is the only way to observe the
	// clamp — the service returns whatever the store hands back.
	lastTake int
	err      error
}

func (f *fakeStore) Insert(_ context.Context, e domain.Entry) error {
	if f.err != nil {
		return f.err
	}
	f.inserted = append(f.inserted, e)
	return nil
}

func (f *fakeStore) List(_ context.Context, _ string, take int) ([]domain.Entry, error) {
	f.lastTake = take
	if f.err != nil {
		return nil, f.err
	}
	return f.feed, nil
}

func newService(store *fakeStore) *Service {
	sequence := 0
	return New(store, func() string {
		sequence++
		return "id-" + string(rune('0'+sequence))
	})
}

func TestRecordStoresTheFactItWasGiven(t *testing.T) {
	store := &fakeStore{}

	err := newService(store).Record(
		context.Background(), "account-1", "SignedIn", "laptop", "10.0.0.1", occurred)

	if err != nil {
		t.Fatalf("Record returned an error: %v", err)
	}
	if len(store.inserted) != 1 {
		t.Fatalf("store holds %d entries, want 1", len(store.inserted))
	}

	got := store.inserted[0]
	if got.UserID != "account-1" || got.Kind != "SignedIn" ||
		got.Device != "laptop" || got.IPAddress != "10.0.0.1" || !got.OccurredAt.Equal(occurred) {
		t.Errorf("stored %+v, want the fact as passed", got)
	}
	if got.ID == "" {
		t.Error("stored entry has no id")
	}
}

func TestRecordWithoutAnAccountNeverReachesTheStore(t *testing.T) {
	store := &fakeStore{}

	err := newService(store).Record(
		context.Background(), "", "SignedIn", "laptop", "10.0.0.1", occurred)

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if len(store.inserted) != 0 {
		t.Errorf("store holds %d entries, want none", len(store.inserted))
	}
}

func TestListClampsTheTake(t *testing.T) {
	cases := []struct {
		name string
		take int
		want int
	}{
		{"zero means the default", 0, 50},
		{"negative means the default", -3, 50},
		{"absurd means the default", 10_000, 50},
		{"reasonable is honoured", 5, 5},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			store := &fakeStore{}
			if _, err := newService(store).List(context.Background(), "account-1", c.take); err != nil {
				t.Fatalf("List returned an error: %v", err)
			}
			if store.lastTake != c.want {
				t.Errorf("store asked for %d, want %d", store.lastTake, c.want)
			}
		})
	}
}

func TestListDropsTheIdentifiersThatAreNobodyElsesBusiness(t *testing.T) {
	store := &fakeStore{feed: []domain.Entry{{
		ID:         "id-1",
		UserID:     "account-1",
		Kind:       "SignedIn",
		Device:     "laptop",
		IPAddress:  "10.0.0.1",
		OccurredAt: occurred,
	}}}

	entries, err := newService(store).List(context.Background(), "account-1", 10)
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if len(entries) != 1 {
		t.Fatalf("got %d entries, want 1", len(entries))
	}
	// activityapi.Entry has no ID and no UserID field, so this is really a statement that the
	// mapping stays complete: everything the feed renders, nothing the module must keep to itself.
	got := entries[0]
	if got.Kind != "SignedIn" || got.Device != "laptop" ||
		got.IPAddress != "10.0.0.1" || !got.OccurredAt.Equal(occurred) {
		t.Errorf("entry = %+v, want the stored fact", got)
	}
}

func TestStoreFailuresPropagateUnchanged(t *testing.T) {
	broken := errors.New("mongo is down")
	store := &fakeStore{err: broken}
	svc := newService(store)

	recordErr := svc.Record(
		context.Background(), "account-1", "SignedIn", "laptop", "10.0.0.1", occurred)
	_, listErr := svc.List(context.Background(), "account-1", 10)

	if !errors.Is(recordErr, broken) {
		t.Errorf("Record returned %v, want the store's error", recordErr)
	}
	if !errors.Is(listErr, broken) {
		t.Errorf("List returned %v, want the store's error", listErr)
	}
}
