package service

import (
	"context"
	"time"

	healthapi "__GO_MODULE__/server/internal/modules/health/api"
)

// probeTimeout bounds one dependency check. Shorter than any client's own deadline on purpose: a
// stalled store should come back as its row saying no, not as the whole call timing out.
const probeTimeout = 2 * time.Second

// System reports the process and whether each dependency answers.
//
// A dependency failing makes its entry OK = false and never an error return: the check succeeding
// is a different fact from the stack being up, and conflating them would make the card that renders
// this go blank exactly when it is most needed. The error itself is discarded rather than carried —
// the route is public, and connection errors are where addresses leak from.
func (s *Service) System(ctx context.Context) (healthapi.SystemStatus, error) {
	probes := []struct {
		name  string
		check func(context.Context) error
	}{
		{"SQL Server", s.store.PingSQL},
		{"MongoDB", s.store.PingMongo},
	}

	deps := make([]healthapi.Dependency, 0, len(probes))
	for _, probe := range probes {
		probeCtx, cancel := context.WithTimeout(ctx, probeTimeout)
		started := s.now()
		err := probe.check(probeCtx)
		cancel()

		deps = append(deps, healthapi.Dependency{
			Name:    probe.name,
			OK:      err == nil,
			Latency: s.now().Sub(started),
		})
	}

	return healthapi.SystemStatus{
		Version:      s.version,
		StartedAt:    s.startedAt,
		ServerTime:   s.now().UTC(),
		Dependencies: deps,
	}, nil
}
