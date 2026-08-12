// Package service reconciles the stored layout against what the build declares, answers what a
// caller's pane looks like, and lets an administrator rearrange it. reconcile.go runs at boot,
// build.go answers the read every client makes, admin.go and apply.go are the write surface; this
// file holds the store and the cache they share.
package service

import (
	"context"
	"sync"
	"time"

	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
)

// Store is the persistence these use cases need, declared here rather than in store/.
type Store interface {
	// Layout reads the headings and the placements as one consistent snapshot.
	Layout(ctx context.Context) ([]domain.Group, []domain.Placement, error)

	Groups(ctx context.Context) ([]domain.Group, error)
	Group(ctx context.Context, id string) (domain.Group, error)
	InsertGroup(ctx context.Context, g domain.Group, at time.Time) error
	UpdateGroup(ctx context.Context, g domain.Group, at time.Time) error
	DeleteGroup(ctx context.Context, id string) error
	EnsureGroup(ctx context.Context, g domain.Group, at time.Time) error

	Placements(ctx context.Context) ([]domain.Placement, error)
	Placement(ctx context.Context, id string) (domain.Placement, error)
	EnsurePlacements(ctx context.Context, seeds []domain.Placement, at time.Time) error
	Move(ctx context.Context, id, groupID string, order int, at time.Time) error
	Override(ctx context.Context, id, title, icon string, isVisible bool, at time.Time) error
	DeleteItem(ctx context.Context, id string) error

	// ApplyLayout writes a whole rearrangement in one transaction, or writes none of it.
	ApplyLayout(ctx context.Context, plan domain.LayoutPlan, at time.Time) error
}

// Clock is injected so a test can pin it.
type Clock func() time.Time

type Service struct {
	store Store
	now   Clock

	// declared is what this build has, in declaration order, and byID is the same thing keyed.
	// Fixed at construction, from the composition root: nothing at runtime can add a destination,
	// which is the same statement as "nothing at runtime can add an unprotected page".
	declared []navigationapi.Destination
	byID     map[string]navigationapi.Destination

	// The layout cache. Every signed-in client reads the pane once per sign-in and the answer is
	// two small tables that change when an administrator says so.
	//
	// A mutex and a nil, rather than a version counter: this server is one process by design, so
	// "reload on the next read" is the whole invalidation protocol. Scaling out would need the
	// writes to publish, and that is when a counter earns its keep.
	mu     sync.RWMutex
	cached *layout

	// roleGrants answers what a role may do, for previewing somebody else's pane. Optional: a build
	// with no authorization module has no roles.
	roleGrants RoleGrants
}

type layout struct {
	groups     []domain.Group
	placements map[string]domain.Placement
}

// A nil clock means time.Now. The destinations usually arrive later, through WithDestinations: they
// are collected from every module once all of them have started, which is after this is built.
func New(st Store, now Clock, declared ...navigationapi.Destination) *Service {
	if now == nil {
		now = time.Now
	}

	svc := &Service{store: st, now: now}
	svc.WithDestinations(declared...)
	return svc
}

func (s *Service) WithDestinations(declared ...navigationapi.Destination) {
	byID := make(map[string]navigationapi.Destination, len(declared))
	for _, d := range declared {
		byID[d.ID] = d
	}

	s.declared = declared
	s.byID = byID
}

// The double check is not premature: between releasing the read lock and taking the write one,
// another caller may already have loaded it, and reloading would be two queries answering the same
// question.
func (s *Service) layoutOf(ctx context.Context) (*layout, error) {
	s.mu.RLock()
	cached := s.cached
	s.mu.RUnlock()
	if cached != nil {
		return cached, nil
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if s.cached != nil {
		return s.cached, nil
	}

	// One snapshot, not two reads. Taken separately, a CreateGroup landing between them produced a
	// layout whose placements named a heading the groups half had not seen — and Build drops a
	// destination whose heading it cannot find, so a screen vanished from every pane until the next
	// write invalidated the cache.
	groups, stored, err := s.store.Layout(ctx)
	if err != nil {
		return nil, err
	}

	placements := make(map[string]domain.Placement, len(stored))
	for _, p := range stored {
		placements[p.DestinationID] = p
	}

	s.cached = &layout{groups: groups, placements: placements}
	return s.cached, nil
}

func (s *Service) invalidate() {
	s.mu.Lock()
	s.cached = nil
	s.mu.Unlock()
}
