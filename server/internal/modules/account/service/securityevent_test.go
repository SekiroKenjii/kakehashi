package service

import (
	"context"
	"testing"
)

func TestSecurityEventsClampsTheTake(t *testing.T) {
	store := newFakeStore()
	account := store.seed(t, "ada@example.com")
	svc := newService(store)
	for range 5 {
		_, _ = svc.Authenticate(context.Background(), "ada@example.com", "wrong", "", "")
	}

	events, err := svc.SecurityEvents(context.Background(), account.ID, -3)
	if err != nil {
		t.Fatalf("SecurityEvents returned an error: %v", err)
	}

	if len(events) != 5 {
		t.Errorf("got %d events, want the 5 recorded", len(events))
	}
}
