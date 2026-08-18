// Package service implements the health use cases. It is private to the module.
package service

import (
	"context"
	"time"

	healthapi "__GO_MODULE__/server/internal/modules/health/api"
)

// Clock hands the service the current time.
//
// It exists so tests can pin "now" instead of asserting on a range. The production value is
// time.Now, and that is the only place the real clock is read.
type Clock func() time.Time

// Store is the persistence seam System checks and nothing here reads: one probe per store the
// process depends on.
type Store interface {
	PingSQL(ctx context.Context) error
	PingMongo(ctx context.Context) error
}

// Service is the healthapi.Service implementation.
type Service struct {
	now       Clock
	store     Store
	version   string
	startedAt time.Time
}

// New builds the service. Pass nil for clock to use the wall clock. The service's construction is
// the process start it reports: the kernel builds modules once, at boot.
func New(clock Clock, version string, store Store) *Service {
	if clock == nil {
		clock = time.Now
	}
	return &Service{now: clock, store: store, version: version, startedAt: clock().UTC()}
}

// Ping echoes the message back with the server's clock.
//
// It deliberately touches neither store. A health check that fails when the database is slow tells
// you the database is slow, not whether this process is alive, and an orchestrator that restarts
// the process in response has made an outage worse rather than better. Storage readiness is a
// separate question and deserves a separate endpoint.
func (s *Service) Ping(_ context.Context, message string) (healthapi.Status, error) {
	return healthapi.Status{
		Message:    message,
		ServerTime: s.now().UTC(),
	}, nil
}

var _ healthapi.Service = (*Service)(nil)
