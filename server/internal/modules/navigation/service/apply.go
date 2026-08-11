package service

import (
	"context"
	"strings"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Applying a whole arrangement at once: staged on the client, applied in one atomic call —
// docs/adr/0004-staged-edits-atomic-apply.md. The whole arrangement is validated before any row is
// written — one transaction, one cache invalidation — so a refusal leaves the stored layout
// exactly as it was.

// GroupSpec is a heading as an administrator wants it. An empty ID means "create it".
type GroupSpec struct {
	ID    string
	Title string
	Order int
}

// ItemSpec is a destination as an administrator wants it placed.
type ItemSpec struct {
	ID      string
	GroupID string
	Order   int

	// Title and Icon are overrides. Empty clears one and returns the destination to what the code
	// calls it.
	Title string
	Icon  string

	IsVisible bool
}

// ApplyOutcome counts what changed, which is not what was sent: a screen posts its whole arrangement
// and most of it is usually already true.
type ApplyOutcome struct {
	GroupsCreated int
	GroupsUpdated int
	GroupsDeleted int
	ItemsChanged  int
}

// ApplyLayout writes a whole arrangement, or writes none of it.
func (s *Service) ApplyLayout(
	ctx context.Context, groups []GroupSpec, items []ItemSpec,
) (ApplyOutcome, error) {
	stored, err := s.layoutOf(ctx)
	if err != nil {
		return ApplyOutcome{}, err
	}

	plan, outcome, err := s.planGroups(groups, stored)
	if err != nil {
		return ApplyOutcome{}, err
	}
	if err := s.planItems(items, stored, plan, &outcome); err != nil {
		return ApplyOutcome{}, err
	}

	// Nothing to do, and nothing to open a transaction for. A screen that posts an unchanged
	// arrangement gets a successful answer with four zeroes, which is the truthful one.
	if plan.IsEmpty() {
		return ApplyOutcome{}, nil
	}

	if err := s.store.ApplyLayout(ctx, *plan, s.now()); err != nil {
		return ApplyOutcome{}, err
	}

	s.invalidate()
	return outcome, nil
}

// planGroups turns the wanted headings into creates, updates and deletes.
func (s *Service) planGroups(
	specs []GroupSpec, stored *layout,
) (*domain.LayoutPlan, ApplyOutcome, error) {
	var (
		plan    domain.LayoutPlan
		outcome ApplyOutcome
	)

	existing := make(map[string]domain.Group, len(stored.groups))
	for _, g := range stored.groups {
		existing[g.ID] = g
	}

	wanted := make(map[string]struct{}, len(specs))
	titles := make(map[string]string, len(specs))

	for _, spec := range specs {
		// IsSystem is read from what is stored rather than taken from the request. A heading the
		// product ships must not become deletable by being submitted as an ordinary one.
		isSystem := false
		if current, ok := existing[spec.ID]; ok {
			isSystem = current.IsSystem
		}

		group, err := domain.NewGroup(spec.ID, spec.Title, spec.Order, isSystem)
		if err != nil {
			return nil, ApplyOutcome{}, err
		}

		if _, seen := wanted[group.ID]; seen {
			return nil, ApplyOutcome{}, errs.Invalidf(
				"Two headings in this arrangement have the identifier %s.", group.ID)
		}
		wanted[group.ID] = struct{}{}

		// Titles are unique in the database, so a collision would surface as a conflict from the
		// middle of a transaction naming one row. Caught here it names both, before anything is
		// written. Compared case-insensitively because the database's collation is.
		folded := strings.ToLower(group.Title)
		if other, clash := titles[folded]; clash {
			return nil, ApplyOutcome{}, errs.Invalidf(
				"%s and %s cannot both be called %s.", other, group.ID, group.Title)
		}
		titles[folded] = group.ID

		current, ok := existing[group.ID]
		switch {
		case !ok:
			plan.CreateGroups = append(plan.CreateGroups, group)
			outcome.GroupsCreated++
		case current.Title != group.Title || current.Order != group.Order:
			plan.UpdateGroups = append(plan.UpdateGroups, group)
			outcome.GroupsUpdated++
		}
	}

	// Absent means deleted. The destinations under a deleted heading fall to ungrouped, which the
	// foreign key does rather than a statement here.
	for _, g := range stored.groups {
		if _, keep := wanted[g.ID]; keep {
			continue
		}
		if g.IsSystem {
			// The same refusal DeleteGroup gives, and for the same reason: a deployment that deleted
			// the heading its administrative screens live under would have nowhere left to put them.
			return nil, ApplyOutcome{}, errs.Invalidf(
				"%s is one of the headings this product ships, so it cannot be deleted. Rename it, "+
					"or move what is under it somewhere else.", g.Title)
		}
		plan.DeleteGroups = append(plan.DeleteGroups, g.ID)
		outcome.GroupsDeleted++
	}

	return &plan, outcome, nil
}

// planItems adds the placements that differ from what is stored.
//
// A stored row absent from the request is left exactly as it is. Destinations come from code, so
// absence here means "not mentioned" and never "remove it" — the opposite of how headings work, and
// the asymmetry is deliberate: an administrator owns the headings and the code owns the screens.
func (s *Service) planItems(
	specs []ItemSpec, stored *layout, plan *domain.LayoutPlan, outcome *ApplyOutcome,
) error {
	// The headings this arrangement will end with, which is what an item may be placed under — not
	// the ones stored now. A screen that creates a heading and drops a destination into it in one
	// gesture is the ordinary case, and checking against the stored set would refuse it.
	available := make(map[string]struct{}, len(plan.CreateGroups)+len(stored.groups))
	for _, g := range plan.CreateGroups {
		available[g.ID] = struct{}{}
	}
	for _, g := range plan.UpdateGroups {
		available[g.ID] = struct{}{}
	}
	for _, g := range stored.groups {
		available[g.ID] = struct{}{}
	}

	// Headings on their way out, kept separately rather than removed from available. An item still
	// pointing at one is not an error: the schema ungroups whatever was under a deleted heading, and
	// the single-row DeleteGroup relies on exactly that. Refusing here would make the two ways of
	// deleting a heading disagree about what happens to its contents, and a screen would have to
	// renumber every affected row to say something the server already knows.
	deleting := make(map[string]struct{}, len(plan.DeleteGroups))
	for _, id := range plan.DeleteGroups {
		delete(available, id)
		deleting[id] = struct{}{}
	}

	seen := make(map[string]struct{}, len(specs))

	for _, spec := range specs {
		current, ok := stored.placements[spec.ID]
		if !ok {
			return errs.NotFoundf("No navigation item with id %s.", spec.ID)
		}
		if _, twice := seen[spec.ID]; twice {
			return errs.Invalidf("%s appears twice in this arrangement.", spec.ID)
		}
		seen[spec.ID] = struct{}{}

		groupID := spec.GroupID
		if groupID != "" {
			// Validated in Go before the write, because the database is more forgiving than the pane:
			// SQL Server compares case-insensitively, so the foreign key accepts "Administration" for
			// the heading whose id is "administration" — and then Build, which matches exactly, finds
			// no such heading and drops the destination out of every pane it belonged in.
			if err := domain.ValidateSlug(groupID); err != nil {
				return err
			}

			if _, gone := deleting[groupID]; gone {
				groupID = ""
			} else if _, exists := available[groupID]; !exists {
				return errs.NotFoundf("No navigation heading with id %s.", groupID)
			}
		}

		if !spec.IsVisible {
			// The one hide that cannot be undone: Build skips an invisible destination before it
			// checks anything, so hiding the screen that manages the pane removes the only surface
			// that could unhide it, from every client at once.
			if d, declared := s.byID[spec.ID]; declared && d.HideWhenDenied {
				return errs.Invalidf(
					"%s is shown only to accounts that hold its permission, so it cannot also be "+
						"hidden by hand. Take the permission away instead, and it disappears for "+
						"everyone who lacks it.",
					d.DefaultTitle)
			}
		}

		title, err := domain.NormaliseOverride("navigation label", spec.Title)
		if err != nil {
			return err
		}
		icon, err := domain.NormaliseOverride("navigation icon name", spec.Icon)
		if err != nil {
			return err
		}

		wanted := domain.Placement{
			DestinationID: current.DestinationID,
			ModuleID:      current.ModuleID,
			GroupID:       groupID,
			Title:         title,
			Icon:          icon,
			Order:         spec.Order,
			IsVisible:     spec.IsVisible,
		}
		if wanted == current {
			continue
		}

		plan.Items = append(plan.Items, wanted)
		outcome.ItemsChanged++
	}

	return nil
}
