package service

import (
	"context"
	"testing"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

func TestSessionsMarksTheCaller(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)

	mine, _ := svc.StartSession(context.Background(), account, "desktop", "laptop", "")
	other, _ := svc.StartSession(context.Background(), account, "desktop", "phone", "")

	sessions, err := svc.Sessions(context.Background(), account.ID, mine.ID)
	if err != nil {
		t.Fatalf("Sessions returned an error: %v", err)
	}

	if len(sessions) != 2 {
		t.Fatalf("got %d sessions, want 2", len(sessions))
	}
	for _, sess := range sessions {
		wantCurrent := sess.ID == mine.ID
		if sess.IsCurrent != wantCurrent {
			t.Errorf("session %s IsCurrent = %v, want %v", sess.ID, sess.IsCurrent, wantCurrent)
		}
		if sess.ID == other.ID && sess.IsCurrent {
			t.Error("the other session is marked current")
		}
	}
}

func TestRevokeAllSessionsEndsEverything(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)
	_, _ = svc.StartSession(context.Background(), account, "desktop", "laptop", "")
	_, _ = svc.StartSession(context.Background(), account, "desktop", "phone", "")

	if err := svc.RevokeAllSessions(context.Background(), account.ID); err != nil {
		t.Fatalf("RevokeAllSessions returned an error: %v", err)
	}

	if len(store.sessions) != 0 {
		t.Errorf("%d sessions survive, want none", len(store.sessions))
	}
}

// Leaving and being ended are the same delete and two different facts, announced as two events —
// only the caller knows which one happened: docs/adr/0003-signedout-vs-sessionrevoked.md
func TestLeavingAndBeingEndedAreAnnouncedApart(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)

	var left []accountapi.SignedOut
	var ended []accountapi.SessionRevoked
	eventbus.Subscribe(svc.bus, func(_ context.Context, e accountapi.SignedOut) {
		left = append(left, e)
	})
	eventbus.Subscribe(svc.bus, func(_ context.Context, e accountapi.SessionRevoked) {
		ended = append(ended, e)
	})

	mine, _ := svc.StartSession(context.Background(), account, "desktop", "laptop", "")
	theirs, _ := svc.StartSession(context.Background(), account, "desktop", "phone", "")

	if err := svc.SignOut(context.Background(), account.ID, mine.ID); err != nil {
		t.Fatalf("SignOut returned an error: %v", err)
	}
	if err := svc.RevokeSession(context.Background(), account.ID, theirs.ID); err != nil {
		t.Fatalf("RevokeSession returned an error: %v", err)
	}

	if len(left) != 1 || left[0].SessionID != mine.ID {
		t.Errorf("SignedOut = %+v, want one naming %s", left, mine.ID)
	}
	if len(ended) != 1 || ended[0].SessionID != theirs.ID {
		t.Errorf("SessionRevoked = %+v, want one naming %s", ended, theirs.ID)
	}
	// The owner did both of these, so neither is somebody else acting on the account.
	if len(ended) == 1 && ended[0].ByAdmin {
		t.Error("the owner revoking their own session was announced as an administrator's doing")
	}
}

// An administrator ending somebody else's session announced nothing at all before this, so the one
// screen a person opens to ask whether anyone has been in their account could not show it.
func TestAnAdministratorEndingASessionIsAnnouncedAsSomebodyElse(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)

	var ended []accountapi.SessionRevoked
	eventbus.Subscribe(svc.bus, func(_ context.Context, e accountapi.SessionRevoked) {
		ended = append(ended, e)
	})

	sess, _ := svc.StartSession(context.Background(), account, "desktop", "laptop", "")

	if err := svc.RevokeAccountSession(context.Background(), account.ID, sess.ID); err != nil {
		t.Fatalf("RevokeAccountSession returned an error: %v", err)
	}

	if len(ended) != 1 {
		t.Fatalf("announced %d revocations, want 1", len(ended))
	}
	if !ended[0].ByAdmin {
		t.Error("ByAdmin = false, want true - this is the row that says another person acted")
	}
	if ended[0].SessionID != sess.ID || ended[0].UserID != account.ID {
		t.Errorf("announced %+v, want the account and session that were ended", ended[0])
	}
}

// Ending a session that is not there succeeds and announces nothing.
//
// Live verification found this: the delete was idempotent, which is right, but the announcement was
// unconditional, which made every one of these paths able to state something that had not happened.
// The administrator case is the one that mattered — any session id at all put "somebody else ended
// your session" into an account's feed, which is the single line on that screen a person acts on.
func TestEndingASessionThatIsNotThereAnnouncesNothing(t *testing.T) {
	for _, tc := range []struct {
		name string
		act  func(*Service, string) error
	}{
		{"the owner leaving", func(s *Service, id string) error {
			return s.SignOut(context.Background(), id, "no-such-session")
		}},
		{"the owner revoking", func(s *Service, id string) error {
			return s.RevokeSession(context.Background(), id, "no-such-session")
		}},
		{"an administrator revoking", func(s *Service, id string) error {
			return s.RevokeAccountSession(context.Background(), id, "no-such-session")
		}},
		{"revoking all of none", func(s *Service, id string) error {
			return s.RevokeAllSessions(context.Background(), id)
		}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			store := newFakeStore()
			account := store.seed(t, "ada@example.com")
			svc := newService(store)

			var announced int
			eventbus.Subscribe(svc.bus, func(_ context.Context, _ accountapi.SessionRevoked) {
				announced++
			})
			eventbus.Subscribe(svc.bus, func(_ context.Context, _ accountapi.SignedOut) {
				announced++
			})

			// Still success: a caller ending something already gone got what they asked for.
			if err := tc.act(svc, account.ID); err != nil {
				t.Fatalf("returned an error: %v", err)
			}
			if announced != 0 {
				t.Errorf("announced %d events for a session that was never there, want 0", announced)
			}
			// Nor is it in the account's own audit trail, which the Account page reads.
			events, err := store.SecurityEventsForUser(context.Background(), account.ID, 10)
			if err != nil {
				t.Fatalf("SecurityEventsForUser returned an error: %v", err)
			}
			if len(events) != 0 {
				t.Errorf("recorded %+v, want nothing", events)
			}
		})
	}
}

// A session that IS there is still announced exactly once, so the guard above did not silence the
// path it protects.
func TestEndingASessionThatIsThereStillAnnouncesItOnce(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)

	var ended []accountapi.SessionRevoked
	eventbus.Subscribe(svc.bus, func(_ context.Context, e accountapi.SessionRevoked) {
		ended = append(ended, e)
	})

	sess, _ := svc.StartSession(context.Background(), account, "desktop", "laptop", "")
	if err := svc.RevokeSession(context.Background(), account.ID, sess.ID); err != nil {
		t.Fatalf("RevokeSession returned an error: %v", err)
	}
	// The second call finds nothing to end, which is exactly the double-click a screen produces.
	if err := svc.RevokeSession(context.Background(), account.ID, sess.ID); err != nil {
		t.Fatalf("the second RevokeSession returned an error: %v", err)
	}

	if len(ended) != 1 {
		t.Fatalf("announced %d revocations for one session, want 1", len(ended))
	}
	if ended[0].SessionID != sess.ID {
		t.Errorf("announced %s, want %s", ended[0].SessionID, sess.ID)
	}
}
