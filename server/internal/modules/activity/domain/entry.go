// Package domain holds the activity module's one record type and the rules it enforces.
//
// Entry is an append-only record, not an aggregate root: written once, never changed. No method
// here mutates one and the store has no update statement. Append-only is not the same as
// permanent: an entry is never edited or deleted by anything this code calls, but it expires, on
// a schedule Mongo runs, once it is older than Retention.
//
// There is no transaction anywhere in this module, as a premise rather than a simplification: a
// use case that needs two writes to succeed together does not belong in activity, or does not
// belong in Mongo.
package domain

import (
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Retention is how long an entry is kept before the store deletes it.
//
// It is here rather than in store/ because "ninety days" is a rule about how long somebody can
// look back; the store implements it with a TTL index, and one constant keeps the index and the
// number the screen reports from disagreeing.
const Retention = 90 * 24 * time.Hour

// Entry is one recorded fact about an account.
//
// The fields are exported so the store can map them onto its document; construction goes through
// NewEntry. There are no bson tags here: the document shape is the store's decision, and a Mongo
// field name is permanent in a store that has no migrations, while these names are refactorable.
type Entry struct {
	ID     string
	UserID string
	Kind   string

	// SessionID is empty for facts that have no session: a password change belongs to an account,
	// not to a device, and clearing every session at once names none of them.
	SessionID string

	Device    string
	IPAddress string

	OccurredAt time.Time
}

// NewEntry builds an entry, rejecting the three shapes that would produce a row nobody can use.
//
// Unlike the notes module's constructor, these messages never reach a user: no caller submits an
// entry, a subscriber does, and the subscriber logs the failure and drops it rather than failing
// the request that caused it. Write them for whoever reads the log.
func NewEntry(
	id, userID, kind, sessionID, device, ip string, occurredAt time.Time,
) (Entry, error) {
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
		SessionID:  sessionID,
		Device:     device,
		IPAddress:  ip,
		OccurredAt: occurredAt,
	}, nil
}
