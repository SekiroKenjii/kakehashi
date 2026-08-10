package service

import (
	"context"
	"testing"
)

func TestProfileNeverExposesThePasswordHash(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")

	profile, err := newService(store).Profile(context.Background(), account.ID)
	if err != nil {
		t.Fatalf("Profile returned an error: %v", err)
	}

	// accountapi.Account has no hash field, so this is really a statement about the mapping
	// staying complete: everything the page needs, nothing the module must keep.
	if profile.Email != "ada@example.com" || profile.DisplayName != "Ada Lovelace" {
		t.Errorf("profile = %+v, want the seeded identity", profile)
	}
}

func TestChangePasswordEndsEverySession(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)
	_, _ = svc.StartSession(context.Background(), account, "desktop", "laptop", "")

	err := svc.ChangePassword(
		context.Background(), account.ID, password, "an entirely new passphrase")

	if err != nil {
		t.Fatalf("ChangePassword returned an error: %v", err)
	}
	// The change is usually a response to believing someone else has the password. Sessions that
	// survive it make it cosmetic.
	if len(store.sessions) != 0 {
		t.Errorf("%d sessions survive the password change, want none", len(store.sessions))
	}
	if !store.accounts[account.ID].VerifyPassword("an entirely new passphrase") {
		t.Error("the new password does not verify against the stored account")
	}
}

func TestChangePasswordRefusesTheWrongCurrentPassword(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)
	_, _ = svc.StartSession(context.Background(), account, "desktop", "laptop", "")

	err := svc.ChangePassword(context.Background(), account.ID, "wrong", "a new passphrase")

	if err == nil {
		t.Fatal("ChangePassword succeeded with the wrong current password")
	}
	if len(store.sessions) != 1 {
		t.Errorf("a failed change touched the sessions: %d live, want 1", len(store.sessions))
	}
}
