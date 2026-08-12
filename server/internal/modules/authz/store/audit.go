package store

import (
	"context"
	"database/sql"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Append-only: an insert and a read, deliberately no update or delete, which is what makes the
// trail worth reading.

func (s *SQLServer) AuditEntries(ctx context.Context, take int) ([]domain.AuditEntry, error) {
	q := `
        SELECT TOP (@p1) a.Id, a.OccurredAt, a.ActorId, a.ActorName, a.Action, a.RoleId,
               a.RoleName, a.PermissionKey, a.Detail
        FROM authz.AuditEntry AS a
        ORDER BY a.OccurredAt DESC, a.Id DESC;`

	return collect(ctx, s.db, "list audit entries", q, []any{take}, scanAuditEntry)
}

// A batch rather than one call per change: one Save is one act, and rows sharing one moment and
// one actor are what let a reader see them as the single decision they were.
func (s *SQLServer) InsertAuditEntries(
	ctx context.Context, entries []domain.AuditEntry,
) error {
	const q = `
        INSERT INTO authz.AuditEntry
            (Id, OccurredAt, ActorId, ActorName, Action, RoleId, RoleName, PermissionKey, Detail)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9);`

	// One transaction, because these entries describe one act. An administrator saving eight
	// toggles produces eight rows, and a failure halfway leaves a trail that says four permissions
	// changed when eight did — the specific way an audit trail becomes worse than not having one.
	return s.inTransaction(ctx, func(tx *sql.Tx) error {
		for _, e := range entries {
			_, err := tx.ExecContext(
				ctx, q, e.ID, e.OccurredAt.UTC(), e.ActorID, e.ActorName, e.Action, e.RoleID,
				e.RoleName, e.PermissionKey, e.Detail)
			if err != nil {
				return errs.Internalf(err, "insert audit entry")
			}
		}
		return nil
	})
}

func scanAuditEntry(sc scanner) (domain.AuditEntry, error) {
	var e domain.AuditEntry
	var at time.Time
	err := sc.Scan(&e.ID, &at, &e.ActorID, &e.ActorName, &e.Action, &e.RoleID, &e.RoleName,
		&e.PermissionKey, &e.Detail)
	if err != nil {
		return domain.AuditEntry{}, errs.Internalf(err, "scan audit entry")
	}
	// DATETIME2 carries no time zone and only UTC is ever written, so say so rather than letting a
	// local zone be inferred from a value that never had one.
	e.OccurredAt = at.UTC()
	return e, nil
}
