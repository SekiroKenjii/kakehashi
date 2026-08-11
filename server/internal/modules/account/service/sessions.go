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

// SignOut ends the caller's own session because they asked to leave.
//
// The same delete as RevokeSession, announced differently. Only the caller knows which of the two
// this is, so only the caller can say — and a week later "you signed out" and "a session was
// revoked" are the difference between a reassuring line and one worth acting on.
func (s *Service) SignOut(ctx context.Context, userID, sessionID string) error {
	ended, err := s.store.DeleteSession(ctx, userID, sessionID)
	if err != nil {
		return err
	}
	if !ended {
		return nil
	}

	s.record(ctx, userID, accountapi.EventSignedOut, "", "")
	eventbus.Publish(s.bus, ctx, accountapi.SignedOut{
		UserID: userID, SessionID: sessionID, At: s.now(),
	})
	return nil
}

// RevokeSession ends one session the owner picked off their device list. Revoking one that is
// already gone succeeds, for the same reason deleting an absent note does — but it announces
// nothing, because a feed that said "a session was revoked" twice for one revocation would be
// describing an event that did not happen.
func (s *Service) RevokeSession(ctx context.Context, userID, sessionID string) error {
	ended, err := s.store.DeleteSession(ctx, userID, sessionID)
	if err != nil {
		return err
	}
	if !ended {
		return nil
	}

	s.record(ctx, userID, accountapi.EventSessionRevoked, "", "")
	eventbus.Publish(s.bus, ctx, accountapi.SessionRevoked{
		UserID: userID, SessionID: sessionID, At: s.now(),
	})
	return nil
}

// RevokeAllSessions ends every session for the account, including the caller's.
func (s *Service) RevokeAllSessions(ctx context.Context, userID string) error {
	ended, err := s.store.DeleteSessionsForUser(ctx, userID)
	if err != nil {
		return err
	}
	if ended == 0 {
		return nil
	}

	// No session id: every one of them went, so naming one would be picking a survivor at random.
	s.record(ctx, userID, accountapi.EventSessionRevoked, "", "")
	eventbus.Publish(s.bus, ctx, accountapi.SessionRevoked{UserID: userID, At: s.now()})
	return nil
}
