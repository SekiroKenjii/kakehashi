// Package domain holds the activity module's one record type and the three rules it enforces.
//
// Entry is an append-only record, not an aggregate root: written once, never changed. There is no
// method here that mutates one and the store has no update statement. The account module's
// domain/doc.go makes the same ruling about its SecurityEvent, and it is true of Entry clause for
// clause.
//
// This module therefore has zero aggregate roots and one record type, which is why there is no
// doc.go — that appears once a package has more than one root to name.
//
// The consequence worth stating: no transaction, ever, anywhere in this module. That is the
// premise rather than a simplification. The moment a use case here needs two writes to succeed
// together, it does not belong in activity, or it does not belong in Mongo.
package domain

import (
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Entry is one recorded fact about an account.
//
// The fields are exported so the store can map them onto its document; construction goes through
// NewEntry. There are no bson tags here: the document shape is the store's decision, and a Mongo
// field name is permanent in a store that has no migrations, while these names are refactorable.
type Entry struct {
	ID     string
	UserID string
	Kind   string

	Device    string
	IPAddress string

	OccurredAt time.Time
}

// NewEntry builds an entry, rejecting the three shapes that would produce a row nobody can use.
//
// Unlike the notes module's constructor, these messages never reach a user: no caller submits an
// entry, a subscriber does, and the subscriber logs the failure and drops it rather than failing
// the request that caused it. Write them for whoever reads the log.
func NewEntry(id, userID, kind, device, ip string, occurredAt time.Time) (Entry, error) {
	if userID == "" {
		// Every read filters on the user id, so an entry without one is unreachable by the only
		// query this module has: silent garbage that nobody will ever see or delete.
		return Entry{}, errs.Invalidf("An activity entry needs the account it belongs to.")
	}
	if kind == "" {
		// The client switches on the kind to choose a label, so an empty one renders a blank row.
		return Entry{}, errs.Invalidf("An activity entry needs a kind.")
	}
	if occurredAt.IsZero() {
		// It is the sort key. A zero time sorts to the bottom of the feed and stays there.
		return Entry{}, errs.Invalidf("An activity entry needs the time it happened.")
	}

	return Entry{
		ID:         id,
		UserID:     userID,
		Kind:       kind,
		Device:     device,
		IPAddress:  ip,
		OccurredAt: occurredAt,
	}, nil
}
