// Package healthapi is the health module's public contract.
//
// Other modules import this package and nothing else under internal/modules/health/. Keep it free
// of implementation: interfaces, plain data, events. No SQL, no protobuf, no other module.
package healthapi

import (
	"context"
	"time"
)

// Status is the answer to a liveness check.
type Status struct {
	// Message is whatever the caller sent, returned unchanged.
	Message string

	// ServerTime is the server's clock, in UTC.
	ServerTime time.Time
}

// Service answers whether the server is alive.
type Service interface {
	// Ping returns message unchanged alongside the server's clock. It never fails: an error here
	// would mean the process cannot run code, in which case nothing would be answering at all.
	Ping(ctx context.Context, message string) (Status, error)
}
