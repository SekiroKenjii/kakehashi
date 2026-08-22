// Package service implements the plugins use cases. It is private to the module.
//
// The files split by use-case family, which is what a caller reaches for together: catalog.go is
// reading what is on offer, download.go is fetching an artifact, publish.go is changing what the
// catalog holds, and install.go is recording what a client did with it. This one is the seam.
package service

import (
	"context"
	"io"
	"time"

	"__GO_MODULE__/server/internal/modules/plugins/domain"
	"__GO_MODULE__/server/internal/platform/eventbus"
)

// Store is the persistence this service needs, declared here rather than in store/.
//
// The interface belongs to the consumer, which is what lets these use cases be tested against a
// fake in microseconds rather than against a container.
type Store interface {
	ListListed(ctx context.Context) ([]domain.Plugin, error)
	GetPlugin(ctx context.Context, pluginID string) (domain.Plugin, error)
	UpsertPlugin(ctx context.Context, p domain.Plugin) error
	SetListed(ctx context.Context, pluginID string, listed bool, now time.Time) error

	LatestVersions(ctx context.Context) (map[string]domain.Version, error)
	ListVersions(ctx context.Context, pluginID string) ([]domain.Version, error)
	GetVersion(ctx context.Context, pluginID, version string) (domain.Version, error)
	InsertVersion(ctx context.Context, v domain.Version, content []byte) error
	SetYanked(ctx context.Context, pluginID, version string, yanked bool) error

	// WriteContent streams an artifact rather than returning it, so neither this service nor the
	// handler above it ever holds a whole package in memory.
	WriteContent(ctx context.Context, pluginID, version string, w io.Writer) error

	InsertInstall(ctx context.Context, userID, pluginID, version, source string, at time.Time) error
}

// Clock hands the service the current time.
//
// It exists so tests can pin "now" instead of asserting on ranges. The production value is
// time.Now, and that is the only place the real clock is read.
type Clock func() time.Time

// Service is the pluginsapi.Service implementation.
type Service struct {
	store Store
	bus   *eventbus.Bus
	now   Clock
}

// New builds the service. Pass nil for clock to use the wall clock.
func New(store Store, bus *eventbus.Bus, clock Clock) *Service {
	if clock == nil {
		clock = time.Now
	}
	return &Service{store: store, bus: bus, now: clock}
}
