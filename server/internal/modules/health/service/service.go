package service

import (
	"context"
	"time"

	healthapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/health/api"
)

// Clock exists so tests can pin "now" instead of asserting on a range. time.Now is read here and
// nowhere else.
type Clock func() time.Time

type Service struct {
	now Clock
}

func New(clock Clock) *Service {
	if clock == nil {
		clock = time.Now
	}
	return &Service{now: clock}
}

// Ping deliberately touches no store. A health check that fails when the database is slow tells you
// the database is slow, not whether this process is alive, and an orchestrator that restarts the
// process in response has made the outage worse. Storage readiness deserves its own endpoint.
func (s *Service) Ping(_ context.Context, message string) (healthapi.Status, error) {
	return healthapi.Status{
		Message:    message,
		ServerTime: s.now().UTC(),
	}, nil
}

var _ healthapi.Service = (*Service)(nil)
