package service

import (
	"context"
	"sort"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// The layout surface. Every write invalidates the cache, and none of them can touch a permission —
// there is no argument anywhere below that reaches what the code declares.

// ItemConfig is a destination as an administrator manages it: the stored row, plus what the build
// says about the same destination so a screen can tell the two apart.
type ItemConfig struct {
	domain.Placement

	DefaultTitle string
	DefaultIcon  string

	// Where the code puts this destination when nothing has moved it.
	//
	// Reported so a screen can offer "reset to what the product shipped". Until this was carried, the
	// answer existed only in the running build: Reconcile writes DefaultGroup and DefaultOrder once,
	// as seeds, and deliberately never re-applies them — so a destination somebody had moved could not
	// be put back through any API.
	DefaultGroup string
	DefaultOrder int

	// Orphan marks a row whose destination is not part of this build.
	Orphan bool

	// What the code enforces. Sent so a screen can explain why something is invisible to somebody,
	// and read-only in the strongest sense available: nothing here writes them.
	RequiredPermission string
	HideWhenDenied     bool
}

// Groups returns every heading, for the administration screen.
func (s *Service) Groups(ctx context.Context) ([]domain.Group, error) {
	stored, err := s.layoutOf(ctx)
	if err != nil {
		return nil, err
	}
	return stored.groups, nil
}

// Items returns every stored placement, orphans included, joined to what the build declares.
//
// Orphans are in the list on purpose: the screen that manages the layout is the only place anybody
// can see that a row's destination is not part of this build, and the only place they can do
// something about it.
func (s *Service) Items(ctx context.Context) ([]ItemConfig, error) {
	stored, err := s.layoutOf(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]ItemConfig, 0, len(stored.placements))

	// Declared first, in declaration order, so the screen reads like the product rather than like
	// the table's clustered index.
	for _, d := range s.declared {
		placement, ok := stored.placements[d.ID]
		if !ok {
			continue
		}
		out = append(out, ItemConfig{
			Placement:          placement,
			DefaultTitle:       d.DefaultTitle,
			DefaultIcon:        d.DefaultIcon,
			DefaultGroup:       d.DefaultGroup,
			DefaultOrder:       d.DefaultOrder,
			RequiredPermission: s.gateOf(d),
			HideWhenDenied:     d.HideWhenDenied,
		})
	}

	var orphans []ItemConfig
	for _, placement := range stored.placements {
		if _, declared := s.byID[placement.DestinationID]; declared {
			continue
		}
		orphans = append(orphans, ItemConfig{Placement: placement, Orphan: true})
	}

	// Sorted: the loop above ranges a map and Go randomises that, so the leftover rows would swap
	// places between refreshes with nothing having changed.
	sort.Slice(orphans, func(i, j int) bool {
		if orphans[i].Order != orphans[j].Order {
			return orphans[i].Order < orphans[j].Order
		}
		return orphans[i].DestinationID < orphans[j].DestinationID
	})

	return append(out, orphans...), nil
}

// CreateGroup adds a heading.
func (s *Service) CreateGroup(ctx context.Context, id, title string, order int) (domain.Group, error) {
	group, err := domain.NewGroup(id, title, order, false)
	if err != nil {
		return domain.Group{}, err
	}
	if err := s.store.InsertGroup(ctx, group, s.now()); err != nil {
		return domain.Group{}, err
	}

	s.invalidate()
	return group, nil
}

// UpdateGroup renames a heading and re-orders it. A system heading may be renamed like any other —
// what it may not be is deleted.
func (s *Service) UpdateGroup(ctx context.Context, id, title string, order int) (domain.Group, error) {
	existing, err := s.store.Group(ctx, id)
	if err != nil {
		return domain.Group{}, err
	}

	group, err := domain.NewGroup(existing.ID, title, order, existing.IsSystem)
	if err != nil {
		return domain.Group{}, err
	}
	if err := s.store.UpdateGroup(ctx, group, s.now()); err != nil {
		return domain.Group{}, err
	}

	s.invalidate()
	return group, nil
}

// DeleteGroup removes a heading an administrator made. The destinations under it fall to ungrouped.
//
// System headings are refused, with the reason rather than a bare no: a deployment that deleted the
// heading its administrative screens live under would have nowhere left to put them, and the person
// clicking the button cannot know that from a 400.
func (s *Service) DeleteGroup(ctx context.Context, id string) error {
	group, err := s.store.Group(ctx, id)
	if err != nil {
		return err
	}
	if group.IsSystem {
		return errs.Invalidf(
			"%s is one of the headings this product ships, so it cannot be deleted. Rename it, or "+
				"move what is under it somewhere else.", group.Title)
	}

	if err := s.store.DeleteGroup(ctx, id); err != nil {
		return err
	}

	s.invalidate()
	return nil
}

// MoveItem changes which heading a destination sits under, and where in it.
func (s *Service) MoveItem(
	ctx context.Context, id, groupID string, order int,
) (ItemConfig, error) {
	// Validated before the write, because the database is more forgiving than the pane. SQL Server
	// compares case-insensitively, so the foreign key accepts "Administration" for the heading
	// whose id is "administration" — and then Build, which matches in Go, finds no heading with
	// that spelling and drops the destination out of every pane it was supposed to appear in.
	if groupID != "" {
		if err := domain.ValidateSlug(groupID); err != nil {
			return ItemConfig{}, err
		}
	}

	if err := s.store.Move(ctx, id, groupID, order, s.now()); err != nil {
		return ItemConfig{}, err
	}

	s.invalidate()
	return s.itemOf(ctx, id)
}

// UpdateItem overrides how a destination reads, and whether it is offered at all.
//
// An empty title or icon clears the override and returns the destination to what the code calls it.
// There has to be a way back, or renaming a page once is permanent.
func (s *Service) UpdateItem(
	ctx context.Context, id, title, icon string, isVisible bool,
) (ItemConfig, error) {
	// The one hide that cannot be undone. Build skips an invisible destination before it checks
	// anything, so hiding the screen that manages the layout removes the only surface that could
	// unhide it — from every client at once, recoverable only by somebody hand-writing an RPC call
	// or an UPDATE. Refused with the reason, the way a system heading refuses deletion.
	if !isVisible {
		if d, declared := s.byID[id]; declared && d.HideWhenDenied {
			return ItemConfig{}, errs.Invalidf(
				"%s is shown only to accounts that hold its permission, so it cannot also be "+
					"hidden by hand. Take the permission away instead, and it disappears for "+
					"everyone who lacks it.",
				d.DefaultTitle)
		}
	}

	title, err := domain.NormaliseOverride("navigation label", title)
	if err != nil {
		return ItemConfig{}, err
	}
	icon, err = domain.NormaliseOverride("navigation icon name", icon)
	if err != nil {
		return ItemConfig{}, err
	}

	if err := s.store.Override(ctx, id, title, icon, isVisible, s.now()); err != nil {
		return ItemConfig{}, err
	}

	s.invalidate()
	return s.itemOf(ctx, id)
}

// itemOf re-reads one placement and joins it to its declaration, so a write returns exactly what a
// subsequent list would show rather than what the caller asked for.
func (s *Service) itemOf(ctx context.Context, id string) (ItemConfig, error) {
	placement, err := s.store.Placement(ctx, id)
	if err != nil {
		return ItemConfig{}, err
	}

	// Keyed by what the STORE returned, never the caller's spelling: SQL Server compares
	// case-insensitively, so "Notes" updates the row id "notes" and the caller's key finds nothing.
	d, declared := s.byID[placement.DestinationID]
	if !declared {
		return ItemConfig{Placement: placement, Orphan: true}, nil
	}
	return ItemConfig{
		Placement:          placement,
		DefaultTitle:       d.DefaultTitle,
		DefaultIcon:        d.DefaultIcon,
		DefaultGroup:       d.DefaultGroup,
		DefaultOrder:       d.DefaultOrder,
		RequiredPermission: s.gateOf(d),
		HideWhenDenied:     d.HideWhenDenied,
	}, nil
}

// DeleteItem removes a stored row whose destination is not part of this build.
//
// Only those. A row whose destination the build still declares would be written straight back by the
// next Reconcile, so deleting it is at best a no-op and at worst one that looks like it worked until
// the server restarts. The refusal says which it is, and points at the thing that does work.
func (s *Service) DeleteItem(ctx context.Context, id string) error {
	stored, err := s.layoutOf(ctx)
	if err != nil {
		return err
	}

	placement, ok := stored.placements[id]
	if !ok {
		return errs.NotFoundf("No navigation item with id %s.", id)
	}

	// Keyed by what the store holds rather than by what the caller passed, for the reason itemOf
	// gives: SQL Server compares case-insensitively and this map does not.
	if d, declared := s.byID[placement.DestinationID]; declared {
		return errs.Invalidf(
			"%s is a screen this build still has, so removing its row would be undone the next time "+
				"the server starts. Hide it instead, or take its permission away.", d.DefaultTitle)
	}

	if err := s.store.DeleteItem(ctx, id); err != nil {
		return err
	}

	s.invalidate()
	return nil
}
