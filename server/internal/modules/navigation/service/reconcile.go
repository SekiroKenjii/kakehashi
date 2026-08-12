package service

import (
	"context"

	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
)

// Reconcile is where the two sources of truth meet: it runs at boot, after the migrations and
// before the server serves.
//
// Three cases, and the difference between them is the whole design:
//
//	a destination with no row      seeded from its declared defaults, so a new module appears in
//	                               the pane the moment it is deployed and nobody has to file it.
//	a destination with a row       left completely alone. The row is an administrator's decision
//	                               and this function is not entitled to it.
//	a row with no destination      left alone too, and skipped when the pane is built. Keeping it
//	                               means a module that comes back — a rollback, a flag turned on
//	                               again — comes back where somebody put it.
//
// The second case is the one worth guarding: a version of this that also refreshed titles and groups
// would undo every rearrangement on every restart, silently, in production.
func (s *Service) Reconcile(ctx context.Context, systemGroups []navigationapi.SystemGroup) error {
	at := s.now()

	// Headings first: a placement names one, so the other order would seed a placement pointing at
	// a heading that is not there yet.
	for _, wanted := range systemGroups {
		group, err := domain.NewGroup(wanted.ID, wanted.Title, wanted.Order, true)
		if err != nil {
			return err
		}
		if err := s.store.EnsureGroup(ctx, group, at); err != nil {
			return err
		}
	}

	seeds := make([]domain.Placement, 0, len(s.declared))
	for _, d := range s.declared {
		seeds = append(seeds, domain.Placement{
			DestinationID: d.ID,
			ModuleID:      d.ModuleID,
			GroupID:       d.DefaultGroup,
			Order:         d.DefaultOrder,
			IsVisible:     true,
		})
	}

	if err := s.store.EnsurePlacements(ctx, seeds, at); err != nil {
		return err
	}

	// Whatever was cached was read before this ran.
	s.invalidate()
	return nil
}
