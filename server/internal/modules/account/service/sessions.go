// The device list on the account page.
//
// Sessions are created in signin.go, not here. A session begins as part of signing in and ends as
// part of account management, and those are different callers — which is also why RevokeSession
// lives here even though the in-app sign-out handler calls it.

package service

import (
	"context"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

// Sessions lists the account's live sessions, marking the caller's own.
func (s *Service) Sessions(
	ctx context.Context, userID, currentID string,
) ([]accountapi.Session, error) {
	sessions, err := s.store.SessionsForUser(ctx, userID)
	if err != nil {
		return nil, err
	}

	out := make([]accountapi.Session, len(sessions))
	for i, sess := range sessions {
		out[i] = accountapi.Session{
			ID:         sess.ID,
			Client:     sess.ClientID,
			Device:     sess.Device,
			IPAddress:  sess.IPAddress,
			CreatedAt:  sess.CreatedAt,
			LastSeenAt: sess.LastSeenAt,
			IsCurrent:  sess.ID == currentID,
		}
	}
	return out, nil
}

// RevokeSession ends one session. Revoking one that is already gone succeeds, for the same reason
// deleting an absent note does.
func (s *Service) RevokeSession(ctx context.Context, userID, sessionID string) error {
	if err := s.store.DeleteSession(ctx, userID, sessionID); err != nil {
		return err
	}

	s.record(ctx, userID, accountapi.EventSessionRevoked, "", "")
	eventbus.Publish(s.bus, ctx, accountapi.SignedOut{
		UserID: userID, SessionID: sessionID, At: s.now(),
	})
	return nil
}

// RevokeAllSessions ends every session for the account, including the caller's.
func (s *Service) RevokeAllSessions(ctx context.Context, userID string) error {
	if err := s.store.DeleteSessionsForUser(ctx, userID); err != nil {
		return err
	}

	s.record(ctx, userID, accountapi.EventSessionRevoked, "", "")
	eventbus.Publish(s.bus, ctx, accountapi.SignedOut{UserID: userID, At: s.now()})
	return nil
}
