package store

import (
	"context"
	"database/sql"
	"errors"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// The headings.

// Groups returns every heading in the order they should be drawn.
//
// Unpaged: the count is how many headings a person felt like making, which is a number a person
// maintains by hand.
// groupsQuery is shared with Layout, so the two readers cannot drift on ordering.
const groupsQuery = `
        SELECT g.Id, g.Title, g.SortOrder, g.IsSystem
        FROM navigation.NavGroup AS g
        ORDER BY g.SortOrder, g.Title;`

// Groups returns every heading, ordered as the pane draws them.
func (s *SQLServer) Groups(ctx context.Context) ([]domain.Group, error) {
	return collect(ctx, s.db, "list navigation groups", groupsQuery, nil, scanGroup)
}

// Group returns one heading.
func (s *SQLServer) Group(ctx context.Context, id string) (domain.Group, error) {
	const q = `
        SELECT g.Id, g.Title, g.SortOrder, g.IsSystem
        FROM navigation.NavGroup AS g
        WHERE g.Id = @p1;`

	group, err := scanGroup(s.db.QueryRowContext(ctx, q, id))
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Group{}, errs.NotFoundf("No navigation heading with id %s.", id)
	}
	return group, err
}

// InsertGroup stores a new heading.
func (s *SQLServer) InsertGroup(ctx context.Context, g domain.Group, at time.Time) error {
	return insertGroupTx(ctx, s.db, g, at)
}

// insertGroupTx is InsertGroup against either handle — see execer.
func insertGroupTx(ctx context.Context, on execer, g domain.Group, at time.Time) error {
	const q = `
        INSERT INTO navigation.NavGroup (Id, Title, SortOrder, IsSystem, CreatedAt, UpdatedAt)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p5);`

	_, err := on.ExecContext(ctx, q, g.ID, g.Title, g.Order, g.IsSystem, at.UTC())
	if isUniqueViolation(err) {
		// Two constraints, two meanings. The key is derived from the title, so "Ops / Tools" and
		// "Ops Tools" collide on ops-tools with different titles — not a title collision.
		if errorContains(err, "PK_NavGroupId") {
			return errs.Conflictf(
				"The identifier %s is already taken by another heading. Give this one an "+
					"identifier of its own.", g.ID)
		}
		return errs.Conflictf("A navigation heading called %s already exists.", g.Title)
	}
	if err != nil {
		return errs.Internalf(err, "insert navigation group")
	}
	return nil
}

// UpdateGroup rewrites a heading's title and order. IsSystem is not editable: a heading the product
// ships cannot become deletable by being edited.
func (s *SQLServer) UpdateGroup(ctx context.Context, g domain.Group, at time.Time) error {
	return updateGroupTx(ctx, s.db, g, at)
}

// updateGroupTx is UpdateGroup against either handle.
func updateGroupTx(ctx context.Context, on execer, g domain.Group, at time.Time) error {
	const q = `
        UPDATE navigation.NavGroup
        SET Title = @p2, SortOrder = @p3, UpdatedAt = @p4
        WHERE Id = @p1;`

	result, err := on.ExecContext(ctx, q, g.ID, g.Title, g.Order, at.UTC())
	if isUniqueViolation(err) {
		return errs.Conflictf("A navigation heading called %s already exists.", g.Title)
	}
	if err != nil {
		return errs.Internalf(err, "update navigation group")
	}
	return requireRow(result, "No navigation heading with id %s.", g.ID)
}

// DeleteGroup removes a heading. The placements under it fall to ungrouped, which the foreign key
// does rather than this method — one less thing that can be forgotten in a second code path.
func (s *SQLServer) DeleteGroup(ctx context.Context, id string) error {
	return deleteGroupTx(ctx, s.db, id)
}

// deleteGroupTx is DeleteGroup against either handle.
func deleteGroupTx(ctx context.Context, on execer, id string) error {
	const q = `DELETE FROM navigation.NavGroup WHERE Id = @p1 AND IsSystem = 0;`

	result, err := on.ExecContext(ctx, q, id)
	if err != nil {
		return errs.Internalf(err, "delete navigation group")
	}
	return requireRow(result, "No deletable navigation heading with id %s.", id)
}

// EnsureGroup seeds a heading if it is not there, and leaves it entirely alone if it is.
//
// Leaving it alone is the half that matters. This runs on every boot, and a version that also
// refreshed the title would undo an administrator's rename every time the process restarted —
// silently, and only in production, where restarts happen without anybody watching.
func (s *SQLServer) EnsureGroup(ctx context.Context, g domain.Group, at time.Time) error {
	const q = `
        IF NOT EXISTS (SELECT 1 FROM navigation.NavGroup AS g WHERE g.Id = @p1)
            INSERT INTO navigation.NavGroup (Id, Title, SortOrder, IsSystem, CreatedAt, UpdatedAt)
            VALUES (@p1, @p2, @p3, @p4, @p5, @p5);`

	_, err := s.db.ExecContext(ctx, q, g.ID, g.Title, g.Order, g.IsSystem, at.UTC())

	// A taken title is not a failure here: IF NOT EXISTS guards the id, but titles are unique too,
	// so a rename onto a shipped title would fail every later boot over a seed nothing would write.
	if isUniqueViolation(err) {
		return nil
	}
	if err != nil {
		return errs.Internalf(err, "seed navigation group %s", g.ID)
	}
	return nil
}

func scanGroup(row scanner) (domain.Group, error) {
	var g domain.Group
	if err := row.Scan(&g.ID, &g.Title, &g.Order, &g.IsSystem); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return domain.Group{}, err
		}
		return domain.Group{}, errs.Internalf(err, "scan navigation group")
	}
	return g, nil
}

// requireRow turns "the statement ran and changed nothing" into a not-found.
//
// An UPDATE that matched no row is not a failure to the driver, so without this a rename of a
// heading somebody else just deleted reports success and does nothing.
func requireRow(result sql.Result, format string, a ...any) error {
	affected, err := result.RowsAffected()
	if err != nil {
		return errs.Internalf(err, "read affected rows")
	}
	if affected == 0 {
		return errs.NotFoundf(format, a...)
	}
	return nil
}
