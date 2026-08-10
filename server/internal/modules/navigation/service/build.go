package service

import (
	"context"
	"sort"

	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// Item is one destination as a caller should see it drawn.
type Item struct {
	ID       string
	ModuleID string
	Title    string
	Icon     string

	// Enabled false means draw it disabled: the product has it, this caller has not been given it.
	Enabled bool
}

// GroupedItems is a heading and what sits under it.
type GroupedItems struct {
	GroupID string
	Title   string
	Items   []Item
}

// Pane is a whole navigation pane for one caller.
type Pane struct {
	// Ungrouped is drawn first, above every heading.
	Ungrouped []Item
	Groups    []GroupedItems
}

// Build returns the pane as this caller should see it.
//
// Placement comes from the database, access from the grants the gate already resolved onto the
// request. Nothing here decides anything about access — it reads the same answer the gate would give
// and turns it into a row that is present, disabled, or absent.
//
// The three outcomes, and why each is what it is:
//
//	permitted                    present and reachable.
//	denied, HideWhenDenied off   present and disabled. A product having something this account has
//	                             not been given is worth being able to see and ask for, which is the
//	                             same argument a server makes by answering 403 rather than 404.
//	denied, HideWhenDenied on    absent. For the destinations where existence is itself
//	                             administrative — a user directory, a permissions matrix — a locked
//	                             row on every screen tells an ordinary account nothing it can act on.
//
// A heading whose every destination came out absent is dropped rather than sent empty, so no client
// has to know that an empty group means "draw nothing".
func (s *Service) Build(ctx context.Context, grants auth.Grants) (Pane, error) {
	stored, err := s.layoutOf(ctx)
	if err != nil {
		return Pane{}, err
	}

	var pane Pane
	byGroup := make(map[string][]Item)

	for _, d := range s.declared {
		placement, ok := stored.placements[d.ID]
		if !ok {
			// Declared but unreconciled — only reachable if a boot failed between the migration and
			// Reconcile. Skipped rather than defaulted, because a pane assembled from a layout that
			// was never written would disagree with the one every other client sees.
			continue
		}
		if !placement.IsVisible {
			continue
		}

		permitted := grants.Allows(s.gateOf(d))
		if !permitted && d.HideWhenDenied {
			continue
		}

		item := Item{
			ID:       d.ID,
			ModuleID: d.ModuleID,
			Title:    coalesce(placement.Title, d.DefaultTitle),
			Icon:     coalesce(placement.Icon, d.DefaultIcon),
			Enabled:  permitted,
		}
		byGroup[placement.GroupID] = append(byGroup[placement.GroupID], item)
	}

	// Within a heading, the stored order. The declaration order decided the loop above, which is
	// only the tie-break for two destinations an administrator gave the same number.
	for group := range byGroup {
		items := byGroup[group]
		sort.SliceStable(items, func(i, j int) bool {
			return stored.placements[items[i].ID].Order < stored.placements[items[j].ID].Order
		})
	}

	pane.Ungrouped = byGroup[""]
	for _, group := range stored.groups {
		items := byGroup[group.ID]
		if len(items) == 0 {
			continue
		}
		pane.Groups = append(pane.Groups, GroupedItems{
			GroupID: group.ID,
			Title:   group.Title,
			Items:   items,
		})
	}
	return pane, nil
}

// gateOf is the permission a destination is checked against.
//
// The declared one, or the owning module's .access when it declares none. Asking authzapi for the
// key rather than concatenating ".access" here: the format is the authorization module's contract,
// and a second copy of it is a copy that stops matching the day it changes.
//
// A destination owned by an UNGATED module has to name its permission. Nobody holds .access for a
// module whose routes are never checked against it, so leaving it empty there would draw a
// permanently disabled row.
func (s *Service) gateOf(d navigationapi.Destination) string {
	if d.Permission != "" {
		return d.Permission
	}
	return authzapi.AccessPermission(d.ModuleID)
}

// coalesce prefers the override, falling back to what the code calls it.
func coalesce(override, fallback string) string {
	if override != "" {
		return override
	}
	return fallback
}
