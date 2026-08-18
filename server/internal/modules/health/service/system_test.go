package service

import (
	"context"
	"errors"
	"testing"
	"time"
)

// fakeStore answers each probe with a scripted error, nil meaning healthy.
type fakeStore struct {
	sqlErr   error
	mongoErr error
}

func (f *fakeStore) PingSQL(context.Context) error   { return f.sqlErr }
func (f *fakeStore) PingMongo(context.Context) error { return f.mongoErr }

func TestSystemReportsEveryDependencyHealthy(t *testing.T) {
	svc := New(func() time.Time { return pinned }, "1.2.3", &fakeStore{})

	status, err := svc.System(context.Background())
	if err != nil {
		t.Fatalf("System returned an error: %v", err)
	}

	if status.Version != "1.2.3" {
		t.Errorf("Version = %q, want %q", status.Version, "1.2.3")
	}
	if !status.StartedAt.Equal(pinned) {
		t.Errorf("StartedAt = %v, want %v", status.StartedAt, pinned)
	}
	if len(status.Dependencies) != 2 {
		t.Fatalf("len(Dependencies) = %d, want 2", len(status.Dependencies))
	}
	if status.Dependencies[0].Name != "SQL Server" || status.Dependencies[1].Name != "MongoDB" {
		t.Errorf("dependency names = %q, %q — want SQL Server, MongoDB",
			status.Dependencies[0].Name, status.Dependencies[1].Name)
	}
	for _, dep := range status.Dependencies {
		if !dep.OK {
			t.Errorf("%s: OK = false, want true", dep.Name)
		}
	}
}

func TestSystemReportsAFailingDependencyNotAnError(t *testing.T) {
	svc := New(func() time.Time { return pinned }, "dev",
		&fakeStore{mongoErr: errors.New("dial tcp: connection refused")})

	status, err := svc.System(context.Background())
	if err != nil {
		t.Fatalf("System returned an error: %v — a down dependency is an answer, not a failure", err)
	}

	if !status.Dependencies[0].OK {
		t.Errorf("SQL Server: OK = false, want true")
	}
	if status.Dependencies[1].OK {
		t.Errorf("MongoDB: OK = true, want false")
	}
}
