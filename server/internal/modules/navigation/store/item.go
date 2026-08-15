package store

import (
	"context"
	"database/sql"
	"errors"
	"time"

	"__GO_MODULE__/server/internal/modules/navigation/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

// The placements: one row per destination, holding where it sits and nothing about what it is.

// Placements returns every stored placement, ordered as the pane draws them.
//
// Every row, including the ones whose destination is not part of this build. Filtering orphans is
// the service's job because only the service knows what is declared, and a store that decided it
// would need the declaration passed in to answer a question about its own table.
// placementsQuery is shared with Layout, so the two readers cannot drift on ordering.
const placementsQuery = `
        SELECT i.Id, i.ModuleId, i.GroupId, i.Title, i.Icon, i.SortOrder, i.IsVisible
        FROM navigation.NavItem AS i
        ORDER BY i.SortOrder, i.Id;`

// Placements returns every stored row, ordered within its heading.
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
			// NULL rather than an empty string when ungrouped, so the foreign key has something legal
			// to point at and "no heading" cannot collide with a heading whose id is empty.
			var group any
			if seed.GroupID != "" {
				group = seed.GroupID
			}

			_, err := tx.ExecContext(
				ctx, q, seed.DestinationID, seed.ModuleID, group, seed.Order,
				seed.IsVisible, at.UTC())
			if isForeignKeyViolation(err) {
				// Reached only if a DefaultGroup names a heading this build does not ship, which
				// Finalize refuses first with a message naming the heading rather than the item.
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
// the prior number happens to fall.
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

// writePlacementTx writes a placement whole: heading, order, overrides and visibility together.
//
// One statement rather than a Move followed by an Override, because ApplyLayout is writing a desired
// state rather than performing two actions. Two statements would also touch UpdatedAt twice and, on
// a failure between them, leave a row half-moved — which is the class of bug ApplyLayout exists to
// make impossible.
func writePlacementTx(
	ctx context.Context, on execer, p domain.Placement, at time.Time,
) error {
	const q = `
        UPDATE navigation.NavItem
        SET GroupId = @p2, SortOrder = @p3, Title = @p4, Icon = @p5, IsVisible = @p6, UpdatedAt = @p7
        WHERE Id = @p1;`

	var group any
	if p.GroupID != "" {
		group = p.GroupID
	}

	result, err := on.ExecContext(
		ctx, q, p.DestinationID, group, p.Order,
		nullable(p.Title), nullable(p.Icon), p.IsVisible, at.UTC())
	if isForeignKeyViolation(err) {
		return errs.NotFoundf("No navigation heading with id %s.", p.GroupID)
	}
	if err != nil {
		return errs.Internalf(err, "write navigation item")
	}
	return requireRow(result, "No navigation item with id %s.", p.DestinationID)
}

// DeleteItem removes a stored placement.
//
// The store will delete any row it is given; only the service knows which rows name a destination
// that is not part of this build, and it refuses the rest. Guarding here as well would mean this
// package needing to know what the build declares, which is the one thing its doc comment says it
// must not.
func (s *SQLServer) DeleteItem(ctx context.Context, id string) error {
	const q = `DELETE FROM navigation.NavItem WHERE Id = @p1;`

	result, err := s.db.ExecContext(ctx, q, id)
	if err != nil {
		return errs.Internalf(err, "delete navigation item")
	}
	return requireRow(result, "No navigation item with id %s.", id)
}
