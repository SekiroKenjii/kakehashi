// Package healthapi is the health module's public contract: other modules import this package and
// nothing else under internal/modules/health/. Interfaces, plain data and events only — no SQL, no
// protobuf, no other module.
package healthapi

import (
	"context"
	"time"
)

type Status struct {
	Message string

	// ServerTime is in UTC.
	ServerTime time.Time
}

type Service interface {
	// Ping echoes message back. The error is always nil: a failure here would mean the process
	// cannot run code, in which case nothing would be answering at all.
	Ping(ctx context.Context, message string) (Status, error)
}
