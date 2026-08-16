package store

import (
	"context"

	"__GO_MODULE__/server/internal/modules/account/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

// InsertSecurityEvent appends to an account's audit trail.
func (s *SQLServer) InsertSecurityEvent(ctx context.Context, e domain.SecurityEvent) error {
	const q = `
        INSERT INTO account.SecurityEvent (Id, AccountId, Kind, Device, IpAddress, OccurredAt)
        VALUES (@p1, @p2, @p3, @p4, @p5, @p6);`

	_, err := s.db.ExecContext(ctx, q, e.ID, e.UserID, e.Kind, e.Device, e.IPAddress,
		storable(e.OccurredAt))
	if err != nil {
		return errs.Internalf(err, "insert security event")
	}
	return nil
}

// SecurityEventsForUser returns the most recent entries, newest first.
func (s *SQLServer) SecurityEventsForUser(
	ctx context.Context, accountID string, take int,
) ([]domain.SecurityEvent, error) {
	const q = `
        SELECT TOP (@p2) se.Id, se.AccountId, se.Kind, se.Device, se.IpAddress, se.OccurredAt
        FROM account.SecurityEvent AS se
        WHERE se.AccountId = @p1
        ORDER BY se.OccurredAt DESC, se.Id DESC;`

	rows, err := s.db.QueryContext(ctx, q, accountID, take)
	if err != nil {
		return nil, errs.Internalf(err, "list security events")
	}
	defer rows.Close()

	var events []domain.SecurityEvent
	for rows.Next() {
		var e domain.SecurityEvent
		if err := rows.Scan(&e.ID, &e.UserID, &e.Kind, &e.Device, &e.IPAddress,
			&e.OccurredAt); err != nil {
			return nil, errs.Internalf(err, "scan security event")
		}
		e.OccurredAt = e.OccurredAt.UTC()
		events = append(events, e)
	}
	if err := rows.Err(); err != nil {
		return nil, errs.Internalf(err, "list security events")
	}
	return events, nil
}
