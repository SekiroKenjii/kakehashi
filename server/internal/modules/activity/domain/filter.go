package domain

import "time"

// Cursor is the position of the last entry a caller has already seen.
//
// Both halves are needed. BSON dates are millisecond-precision, so "older than this timestamp"
// would skip whatever shared the last entry's millisecond, and "older than or equal to it" would
// repeat that entry.
type Cursor struct {
	OccurredAt time.Time
	ID         string
}

// Filter narrows a feed read. Every field's zero value means "do not narrow by this".
//
// In domain rather than beside the store that executes it or the service that builds it, because
// both have to name it: the service owns its Store interface, so a filter declared in the store
// would make the consumer import the implementation, and one declared in the service would point
// the store at the use cases above it.
type Filter struct {
	From time.Time
	To   time.Time

	// In terms of Entry.Kind rather than of a category. A category is a grouping the module's api
	// package decides; neither the store nor this package should change when that grouping does.
	Kinds []string

	// Matches a substring of the kind, the device or the address, case-insensitively.
	Query string

	// Nil starts at the newest entry.
	After *Cursor
}
