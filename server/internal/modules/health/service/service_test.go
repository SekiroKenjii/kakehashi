package service

import (
	"context"
	"testing"
	"time"
)

// pinned is a fixed instant, so the assertion is an equality rather than a range. Reading the real
// clock in a test buys a flake that reproduces once a month, at midnight.
var pinned = time.Date(2026, time.August, 5, 12, 0, 0, 0, time.UTC)

func TestPingEchoesTheMessage(t *testing.T) {
	svc := New(func() time.Time { return pinned }, "dev", nil)

	status, err := svc.Ping(context.Background(), "hello")

	if err != nil {
		t.Fatalf("Ping returned an error: %v", err)
	}
	if status.Message != "hello" {
		t.Errorf("Message = %q, want %q", status.Message, "hello")
	}
}

func TestPingReportsTheClockInUTC(t *testing.T) {
	// A clock in another zone, to prove the service normalises rather than passing it through: the
	// wire type is defined as UTC, and a server in Asia/Ho_Chi_Minh must not report local time.
	saigon := time.FixedZone("ICT", 7*60*60)
	svc := New(func() time.Time { return pinned.In(saigon) }, "dev", nil)

	status, err := svc.Ping(context.Background(), "")
	if err != nil {
		t.Fatalf("Ping returned an error: %v", err)
	}

	if !status.ServerTime.Equal(pinned) {
		t.Errorf("ServerTime = %v, want %v", status.ServerTime, pinned)
	}
	if status.ServerTime.Location() != time.UTC {
		t.Errorf("ServerTime location = %v, want UTC", status.ServerTime.Location())
	}
}

func TestNewDefaultsToTheWallClock(t *testing.T) {
	before := time.Now().UTC()

	status, err := New(nil, "dev", nil).Ping(context.Background(), "")
	if err != nil {
		t.Fatalf("Ping returned an error: %v", err)
	}

	if status.ServerTime.Before(before) {
		t.Errorf("ServerTime = %v, want at or after %v", status.ServerTime, before)
	}
}
