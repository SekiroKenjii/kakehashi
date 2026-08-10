// Package service implements the activity use cases. It is private to the module.
//
// One file, because there is one use-case family and it has two members: append a fact, read the
// feed back. They are the two halves of one question, exactly as the account module's audit trail
// is.
//
// Nothing here imports another module. The whole foreign vocabulary of this package is its own
// domain type, which is what keeps its tests free of any other module's events.
package service

import (
	"context"
	"time"

	"github.com/google/uuid"

	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/domain"
)

// Store is the persistence these use cases need, declared here rather than in store/.
//
// The interface belongs to the consumer, which is what lets these use cases be tested against a
// fake in microseconds. See the notes module for the longer argument; it applies unchanged, and
// here it does one more thing — it keeps a package that must never import another module from
// naming anything but its own domain.
type Store interface {
	Insert(ctx context.Context, e domain.Entry) error
	List(ctx context.Context, userID string, take int) ([]domain.Entry, error)
}

// IDs mints an entry identifier. Injected so tests can pin it instead of asserting on shapes.
type IDs func() string

// Service implements activityapi.Service, plus the Record that no other module may reach.
type Service struct {
	store Store
	newID IDs
}

// New builds the service. Pass nil for ids to use random UUIDs.
func New(store Store, ids IDs) *Service {
	if ids == nil {
		ids = uuid.NewString
	}
	return &Service{store: store, newID: ids}
}

// Record appends one fact to an account's feed.
//
// It is absent from activityapi.Service on purpose: a feed another module can write into is a feed
// every module must call, which is the inverted dependency this module exists to avoid. The
// account module's sign-in use cases are withheld from its own interface for the same reason.
//
// `at` is when the fact happened, not when the row was written. Those are the same instant today,
// because the bus delivers synchronously on the publisher's goroutine — naming the parameter for
// the fact is what keeps the feed correct the first time a handler defers its work.
func (s *Service) Record(
	ctx context.Context, userID, kind, device, ip string, at time.Time,
) error {
	// Ask the domain, then the store. The service orchestrates; it does not re-implement the rules.
	entry, err := domain.NewEntry(s.newID(), userID, kind, device, ip, at)
	if err != nil {
		return err
	}
	return s.store.Insert(ctx, entry)
}

// List returns the account's most recent entries, newest first.
func (s *Service) List(
	ctx context.Context, userID string, take int,
) ([]activityapi.Entry, error) {
	if take <= 0 || take > 200 {
		// Clamped rather than rejected: the parameter comes off the wire, and a client that asks
		// for a million rows deserves an answer rather than an error. The same numbers as the
		// account module's security events, deliberately — the two reads answer the same question
		// and should not answer it differently.
		take = 50
	}

	entries, err := s.store.List(ctx, userID, take)
	if err != nil {
		return nil, err
	}

	out := make([]activityapi.Entry, len(entries))
	for i, e := range entries {
		out[i] = toAPI(e)
	}
	return out, nil
}

// toAPI is the border checkpoint: nothing crosses out of the module without passing through here,
// and the id and the account it belongs to stop at it.
func toAPI(e domain.Entry) activityapi.Entry {
	return activityapi.Entry{
		Kind:       e.Kind,
		Device:     e.Device,
		IPAddress:  e.IPAddress,
		OccurredAt: e.OccurredAt,
	}
}

var _ activityapi.Service = (*Service)(nil)
