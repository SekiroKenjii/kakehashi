// Package activityapi is the activity module's public contract.
//
// Other modules import this package and nothing else under internal/modules/activity/. Note what
// is absent: there is no Record. Entries are written by this module reacting to facts the other
// modules announce, and a feed anyone may append to is a feed everyone must call — which is the
// dependency direction the module exists to avoid.
package activityapi

import (
	"context"
	"time"
)

// Feed entry kinds.
//
// These strings cross the wire and the client switches on them to choose a label and an icon, so
// renaming one silently degrades the feed to showing the raw value. Treat them as contract.
//
// They are declared here rather than imported from another module's api because an api package may
// not import another module at all. The duplication is the feature: it lets the account module
// rename its own audit vocabulary without renaming the feed's, and one line in subscriptions.go is
// the entire cost of keeping the two independent.
const (
	KindSignedIn  = "SignedIn"
	KindSignedOut = "SignedOut"

	// KindPasswordChanged has an audit counterpart in the account module; KindSignedOut above does
	// not. That asymmetry is the proof that this vocabulary is its own rather than a re-export.
	KindPasswordChanged = "PasswordChanged"
)

// Entry is one thing that happened to an account.
//
// No identifier and no user id: neither crosses the wire and neither is another module's business.
// The id belongs to the store, and the user id is the query's filter rather than its result.
type Entry struct {
	// Kind is one of the Kind* constants above.
	Kind string

	// Device is whatever the user agent claimed when the entry was recorded. Untrusted, and shown
	// only as a hint — the reader is asking "was that me?", not "what browser was that".
	Device string

	IPAddress string

	// OccurredAt is when the fact happened, which is neither when it was stored nor when it was
	// read.
	OccurredAt time.Time
}

// Service is the activity module's read surface. There is no write surface.
type Service interface {
	// List returns the account's most recent entries, newest first. take is clamped, never
	// rejected.
	List(ctx context.Context, userID string, take int) ([]Entry, error)
}
