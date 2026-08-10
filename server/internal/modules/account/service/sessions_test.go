package service

import (
	"context"
	"testing"
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
