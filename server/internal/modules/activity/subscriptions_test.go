package activity

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

var happenedAt = time.Date(2026, time.August, 6, 9, 30, 0, 0, time.UTC)

type fakeStore struct {
	inserted []domain.Entry
	err      error
}

func (f *fakeStore) Insert(_ context.Context, e domain.Entry) error {
	if f.err != nil {
		return f.err
	}
	f.inserted = append(f.inserted, e)
	return nil
}

func (f *fakeStore) List(context.Context, string, int) ([]domain.Entry, error) {
	return nil, nil
}

// newModule wires just enough kernel to publish on. k.SQL and k.Mongo stay nil and are never
// touched, because subscribe reads only the bus and the service is built over the fake — which is
// also why Register must not be called here: it would hand the real store a nil handle.
func newModule(store *fakeStore) (*Module, *app.Kernel) {
	log := slog.New(slog.NewTextHandler(io.Discard, nil))
	kernel := app.NewKernel(log, nil, nil, nil, eventbus.New(log))

	module := &Module{
		log: log,
		svc: service.New(store, func() string { return "id-1" }),
	}
	module.subscribe(kernel)
	return module, kernel
}

func TestEachAccountFactBecomesOneEntry(t *testing.T) {
	cases := []struct {
		name      string
		publish   func(*app.Kernel)
		wantKind  string
		wantWhere string
		wantIP    string
	}{
		{
			name: "signed in",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SignedIn{
					UserID:    "account-1",
					Device:    "laptop",
					IPAddress: "10.0.0.1",
					At:        happenedAt,
				})
			},
			wantKind: "SignedIn", wantWhere: "laptop", wantIP: "10.0.0.1",
		},
		{
			name: "signed out",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SignedOut{
					UserID: "account-1", At: happenedAt,
				})
			},
			// The event carries neither, and inventing one would be a row that lies about where
			// the sign-out came from.
			wantKind: "SignedOut", wantWhere: "", wantIP: "",
		},
		{
			name: "password changed",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.PasswordChanged{
					UserID: "account-1", At: happenedAt,
				})
			},
			wantKind: "PasswordChanged", wantWhere: "", wantIP: "",
		},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			store := &fakeStore{}
			_, kernel := newModule(store)

			c.publish(kernel)

			if len(store.inserted) != 1 {
				t.Fatalf("stored %d entries, want 1", len(store.inserted))
			}
			got := store.inserted[0]
			if got.Kind != c.wantKind {
				t.Errorf("Kind = %q, want %q", got.Kind, c.wantKind)
			}
			if got.Device != c.wantWhere || got.IPAddress != c.wantIP {
				t.Errorf("Device/IP = %q/%q, want %q/%q",
					got.Device, got.IPAddress, c.wantWhere, c.wantIP)
			}
			if !got.OccurredAt.Equal(happenedAt) {
				t.Errorf("OccurredAt = %v, want the event's own time %v", got.OccurredAt, happenedAt)
			}
			// The whole point of the module: the entry is filed under the account the event named,
			// which is what makes one machine's sign-in fall inside another machine's feed.
			if got.UserID != "account-1" {
				t.Errorf("UserID = %q, want the event's account", got.UserID)
			}
		})
	}
}

// The structural guarantee, asserted rather than assumed: a dead Mongo cannot fail a sign-in. The
// bus hands the handler no way to return an error and recovers a panic, so Publish returns
// normally whatever the store does.
func TestADeadStoreDoesNotDisturbThePublisher(t *testing.T) {
	store := &fakeStore{err: errors.New("mongo is down")}
	_, kernel := newModule(store)

	eventbus.Publish(kernel.Bus, context.Background(), accountapi.SignedIn{
		UserID: "account-1", Device: "laptop", At: happenedAt,
	})

	if len(store.inserted) != 0 {
		t.Errorf("stored %d entries, want none", len(store.inserted))
	}
}
