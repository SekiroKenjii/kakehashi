package domain

import "time"

// Append-only by construction: there is no method here that changes one, and the store has no
// update statement for the table. An audit trail that can be edited is not one.
type SecurityEvent struct {
	ID     string
	UserID string

	// One of the accountapi.Event* constants. The domain does not enumerate them, because the set
	// is a contract with the client rather than a rule about the world.
	Kind string

	Device     string
	IPAddress  string
	OccurredAt time.Time
}
