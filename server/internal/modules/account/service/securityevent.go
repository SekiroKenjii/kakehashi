// Both halves of the audit trail.
//
// record has callers in all three of the other files but lives beside the read it feeds, because
// which kinds get written and which come back out is one question, not two.

package service

import (
	"context"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
)

// SecurityEvents returns the most recent audit entries.
func (s *Service) SecurityEvents(
	ctx context.Context, userID string, take int,
) ([]accountapi.SecurityEvent, error) {
	if take <= 0 || take > 200 {
		// Clamped rather than rejected: the parameter comes off a query string, and a client that
		// asks for a million rows deserves an answer rather than an error.
		take = 50
	}

	events, err := s.store.SecurityEventsForUser(ctx, userID, take)
	if err != nil {
		return nil, err
	}

	out := make([]accountapi.SecurityEvent, len(events))
	for i, e := range events {
		out[i] = accountapi.SecurityEvent{
			Kind:       e.Kind,
			Device:     e.Device,
			IPAddress:  e.IPAddress,
			OccurredAt: e.OccurredAt,
		}
	}
	return out, nil
}

// record appends to the audit trail. It deliberately swallows its error: failing to write an audit
// row must not fail the sign-in that caused it, and the alternative — refusing to authenticate
// anyone because a log table is full — is worse than a gap in the trail.
func (s *Service) record(ctx context.Context, userID, kind, device, ip string) {
	_ = s.store.InsertSecurityEvent(ctx, domain.SecurityEvent{
		ID:         s.newID(),
		UserID:     userID,
		Kind:       kind,
		Device:     device,
		IPAddress:  ip,
		OccurredAt: s.now(),
	})
}
