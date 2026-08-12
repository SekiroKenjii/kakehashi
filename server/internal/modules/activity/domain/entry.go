// Package domain holds the activity module's one record type and the rules it enforces.
//
// Entry is an append-only record, not an aggregate root: written once, never changed. No method
// here mutates one and the store has no update statement.
//
// Append-only is not the same as permanent. An entry is never edited and never deleted by anything
// this code calls; it expires, on a schedule Mongo runs, once it is older than Retention. Nobody
// rewrites history — history stops going back forever, which is what the screen reports.
//
// No transaction, ever, anywhere in this module. That is the premise rather than a simplification:
// the moment a use case here needs two writes to succeed together, it does not belong in activity,
// or it does not belong in Mongo.
package domain

import (
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// How long an entry is kept. A rule about the record rather than about the storage, which is why it
// is not in store/: the store implements it with a TTL index, and the number lives in one place so
// that index and the number the screen reports cannot come to disagree.
const Retention = 90 * 24 * time.Hour

// Entry is one recorded fact about an account. Construction goes through NewEntry.
//
// No bson tags here: the document shape is the store's decision, and a Mongo field name is
// permanent in a store that has no migrations, while these names are refactorable.
type Entry struct {
	ID     string
	UserID string
	Kind   string

	// Empty for facts that have no session: a password change belongs to an account, not to a
	// device, and clearing every session at once names none of them.
	SessionID string

	Device    string
	IPAddress string

	OccurredAt time.Time
}

// These messages never reach a user: no caller submits an entry, a subscriber does, and it logs the
// failure and drops it rather than failing the request that caused it. Write them for whoever reads
// the log.
func NewEntry(
	id, userID, kind, sessionID, device, ip string, occurredAt time.Time,
) (Entry, error) {
	if userID == "" {
		// Every read filters on the user id, so an entry without one is unreachable by the only
		// query this module has: silent garbage nobody will ever see or delete.
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
