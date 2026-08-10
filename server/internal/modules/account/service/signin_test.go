package service

import (
	"context"
	"testing"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

func TestAuthenticateAcceptsTheRightPassword(t *testing.T) {
	store := newFakeStore()
	seeded := store.seed(t, "ada@example.com")

	account, err := newService(store).Authenticate(
		context.Background(), "ada@example.com", password, "laptop", "10.0.0.1")

	if err != nil {
		t.Fatalf("Authenticate returned an error: %v", err)
	}
	if account.ID != seeded.ID {
		t.Errorf("account = %s, want %s", account.ID, seeded.ID)
	}
}

func TestAuthenticateGivesOneAnswerForBothFailures(t *testing.T) {
	store := newFakeStore()
	store.seed(t, "ada@example.com")
	svc := newService(store)

	_, wrongPassword := svc.Authenticate(
		context.Background(), "ada@example.com", "not the password", "", "")
	_, unknownAddress := svc.Authenticate(
		context.Background(), "nobody@example.com", password, "", "")

	// The two messages must be identical, or the sign-in form becomes an oracle that confirms
	// which addresses have accounts.
	if wrongPassword == nil || unknownAddress == nil {
		t.Fatal("expected both attempts to fail")
	}
	if wrongPassword.Error() != unknownAddress.Error() {
		t.Errorf("messages differ:\n  wrong password:  %v\n  unknown address: %v",
			wrongPassword, unknownAddress)
	}
	if errs.KindOf(wrongPassword) != errs.Unauthenticated {
		t.Errorf("kind = %v, want %v", errs.KindOf(wrongPassword), errs.Unauthenticated)
	}
}

func TestAuthenticateRecordsFailedAttemptsOnTheAccount(t *testing.T) {
	store := newFakeStore()
	store.seed(t, "ada@example.com")

	_, _ = newService(store).Authenticate(
		context.Background(), "ada@example.com", "wrong", "laptop", "10.0.0.1")

	kinds := store.kinds()
	if len(kinds) != 1 || kinds[0] != accountapi.EventFailedSignIn {
		t.Errorf("recorded %v, want exactly one %s", kinds, accountapi.EventFailedSignIn)
	}
}

func TestStartSessionRecordsAndAnnounces(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)

	var announced []accountapi.SignedIn
	eventbus.Subscribe(svc.bus, func(_ context.Context, e accountapi.SignedIn) {
		announced = append(announced, e)
	})

	session, err := svc.StartSession(
		context.Background(), account, "kakehashi-desktop", "laptop", "10.0.0.1")

	if err != nil {
		t.Fatalf("StartSession returned an error: %v", err)
	}
	if session.UserID != account.ID {
		t.Errorf("session.UserID = %s, want %s", session.UserID, account.ID)
	}
	if len(store.sessions) != 1 {
		t.Errorf("store holds %d sessions, want 1", len(store.sessions))
	}
	if len(announced) != 1 || announced[0].SessionID != session.ID {
		t.Errorf("announced %+v, want one SignedIn carrying session %s", announced, session.ID)
	}

	// First sign-in from a device nobody has used: that is the event kind people actually read.
	kinds := store.kinds()
	if len(kinds) != 1 || kinds[0] != accountapi.EventNewDeviceSignedIn {
		t.Errorf("recorded %v, want exactly one %s", kinds, accountapi.EventNewDeviceSignedIn)
	}
}

func TestStartSessionOnAKnownDeviceIsAPlainSignIn(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)

	if _, err := svc.StartSession(
		context.Background(), account, "kakehashi-desktop", "laptop", "10.0.0.1"); err != nil {
		t.Fatalf("first StartSession returned an error: %v", err)
	}
	if _, err := svc.StartSession(
		context.Background(), account, "kakehashi-desktop", "laptop", "10.0.0.2"); err != nil {
		t.Fatalf("second StartSession returned an error: %v", err)
	}

	kinds := store.kinds()
	want := []string{accountapi.EventNewDeviceSignedIn, accountapi.EventSignedIn}
	if len(kinds) != 2 || kinds[0] != want[0] || kinds[1] != want[1] {
		t.Errorf("recorded %v, want %v", kinds, want)
	}
}

func TestCompleteAuthRequestDelegatesWithTheClock(t *testing.T) {
	store := newFakeStore()

	err := newService(store).CompleteAuthRequest(
		context.Background(), "request-9", "account-1", "session-7")

	if err != nil {
		t.Fatalf("CompleteAuthRequest returned an error: %v", err)
	}
	if len(store.completed) != 1 || store.completed[0] != "request-9:account-1:session-7" {
		t.Errorf("completed = %v, want the delegated triple", store.completed)
	}
}
