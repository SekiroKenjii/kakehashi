package store

import (
	"context"
	"database/sql"
	"errors"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// The placements: one row per destination, holding where it sits and nothing about what it is.

// Placements returns every stored placement, ordered as the pane draws them.
//
// Every row, including the ones whose destination this build no longer has. Filtering orphans is the
// service's job because only the service knows what is declared, and a store that decided it would
// need the declaration passed in to answer a question about its own table.
// placementsQuery is shared with Layout, so the two readers cannot drift on ordering.
const placementsQuery = `
        SELECT i.Id, i.ModuleId, i.GroupId, i.Title, i.Icon, i.SortOrder, i.IsVisible
        FROM navigation.NavItem AS i
        ORDER BY i.SortOrder, i.Id;`

func (s *SQLServer) Placements(ctx context.Context) ([]domain.Placement, error) {
	return collect(ctx, s.db, "list navigation items", placementsQuery, nil, scanPlacement)
}

// Placement returns one stored placement.
func (s *SQLServer) Placement(ctx context.Context, id string) (domain.Placement, error) {
	const q = `
        SELECT i.Id, i.ModuleId, i.GroupId, i.Title, i.Icon, i.SortOrder, i.IsVisible
        FROM navigation.NavItem AS i
        WHERE i.Id = @p1;`

	placement, err := scanPlacement(s.db.QueryRowContext(ctx, q, id))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Placement{}, errs.NotFoundf("No navigation item with id %s.", id)
	}
	return placement, err
}

// EnsurePlacements seeds a row for every destination that does not have one yet, in one transaction.
//
// Insert-if-missing, never update. That is the whole reconciliation rule and it is worth being
// blunt about: a deployment gets the arrangement the product intended the first time it sees a
// destination, and keeps the arrangement its administrator chose from then on. A version of this
// that also refreshed the defaults would silently undo every move somebody made, on every restart.
func (s *SQLServer) EnsurePlacements(
	ctx context.Context, seeds []domain.Placement, at time.Time,
) error {
	if len(seeds) == 0 {
		return nil
	}

	return s.inTransaction(ctx, func(tx *sql.Tx) error {
		const q = `
            IF NOT EXISTS (SELECT 1 FROM navigation.NavItem AS i WHERE i.Id = @p1)
                INSERT INTO navigation.NavItem
                    (Id, ModuleId, GroupId, SortOrder, IsVisible, UpdatedAt)
                VALUES (@p1, @p2, @p3, @p4, @p5, @p6);`

		for _, seed := range seeds {
			// The group is written as NULL rather than an empty string when a destination is
			// ungrouped, so the foreign key has something legal to point at — and so "no heading"
			// and "a heading whose id is the empty string" cannot both exist.
			var group any
			if seed.GroupID != "" {
				group = seed.GroupID
			}

			_, err := tx.ExecContext(
				ctx, q, seed.DestinationID, seed.ModuleID, group, seed.Order,
				seed.IsVisible, at.UTC())
			if isForeignKeyViolation(err) {
				// Reached only if a destination's DefaultGroup names a heading this build does not
				// ship — which Finalize refuses first, with a better message. Kept because the
				// alternative is an opaque internal error naming the item rather than the heading.
				return errs.Invalidf(
					"Destination %s seeds into heading %s, which does not exist.",
					seed.DestinationID, seed.GroupID)
			}
			if err != nil {
				return errs.Internalf(err, "seed navigation item %s", seed.DestinationID)
			}
		}
		return nil
	})
}

// Move changes which heading a placement sits under, and where in it.
//
// One method for both because they are one action: an item dropped into a group has landed
// somewhere in it, and a move that set the group and left the order behind would put it wherever
// the old number happens to fall.
func (s *SQLServer) Move(ctx context.Context, id, groupID string, order int, at time.Time) error {
	const q = `
        UPDATE navigation.NavItem
        SET GroupId = @p2, SortOrder = @p3, UpdatedAt = @p4
        WHERE Id = @p1;`

	var group any
	if groupID != "" {
		group = groupID
	}

	result, err := s.db.ExecContext(ctx, q, id, group, order, at.UTC())
	if isForeignKeyViolation(err) {
		return errs.NotFoundf("No navigation heading with id %s.", groupID)
	}
	if err != nil {
		return errs.Internalf(err, "move navigation item")
	}
	return requireRow(result, "No navigation item with id %s.", id)
}

// Override rewrites a placement's label, icon and visibility.
//
// An empty title or icon is stored as NULL, which is what returns the destination to whatever the
// code calls it. Storing the empty string instead would give a page a blank label and no way back.
func (s *SQLServer) Override(
	ctx context.Context, id, title, icon string, isVisible bool, at time.Time,
) error {
	const q = `
        UPDATE navigation.NavItem
        SET Title = @p2, Icon = @p3, IsVisible = @p4, UpdatedAt = @p5
        WHERE Id = @p1;`

	result, err := s.db.ExecContext(ctx, q, id, nullable(title), nullable(icon), isVisible, at.UTC())
	if err != nil {
		return errs.Internalf(err, "override navigation item")
	}
	return requireRow(result, "No navigation item with id %s.", id)
}

func scanPlacement(row scanner) (domain.Placement, error) {
	var (
		p     domain.Placement
		group sql.NullString
		title sql.NullString
		icon  sql.NullString
	)

	err := row.Scan(&p.DestinationID, &p.ModuleID, &group, &title, &icon, &p.Order, &p.IsVisible)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return domain.Placement{}, err
		}
		return domain.Placement{}, errs.Internalf(err, "scan navigation item")
	}

	p.GroupID = group.String
	p.Title = title.String
	p.Icon = icon.String
	return p, nil
}

// nullable turns the empty string into a SQL NULL, because in this table they mean different things.
func nullable(value string) any {
	if value == "" {
		return nil
	}
	return value
}

// isForeignKeyViolation reports whether err is SQL Server refusing a reference to a row that is not
// there — here, a move into a heading that does not exist.
//
// Message matching, for the same reason isUniqueViolation does it.
func isForeignKeyViolation(err error) bool {
	return err != nil && errorContains(err, "FOREIGN KEY constraint")
}
