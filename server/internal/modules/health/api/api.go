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

// SystemStatus is the answer to a storage-readiness check: the process, and each dependency.
type SystemStatus struct {
	// Version is what the binary was built as. "dev" when nothing was injected at build time.
	Version string

	// StartedAt is when the process started, in UTC.
	StartedAt time.Time

	// ServerTime is the server's clock, in UTC.
	ServerTime time.Time

	// Dependencies holds one entry per thing the process needs, in wiring order.
	Dependencies []Dependency
}

// Dependency is one thing the server needs and whether it answered just now.
type Dependency struct {
	// Name is a display name chosen by the wiring, never an address.
	Name string

	// OK is whether the dependency answered within the module's deadline.
	OK bool

	// Latency is how long the answer took. Meaningless when OK is false.
	Latency time.Duration
}

// Service answers whether the server is alive and what it depends on.
type Service interface {
	// Ping returns message unchanged alongside the server's clock. It never fails: an error here
	// would mean the process cannot run code, in which case nothing would be answering at all.
	Ping(ctx context.Context, message string) (Status, error)

	// System reports the process and its dependencies. A dependency that does not answer makes
	// its entry OK = false, never an error: the check succeeding is not the stack being up.
	System(ctx context.Context) (SystemStatus, error)
}
