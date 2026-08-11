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

// The reading half of the store, stubbed: these tests are about what a published fact writes, and
// nothing here ever reads the feed back.
func (f *fakeStore) List(
	context.Context, string, domain.Filter, int,
) ([]domain.Entry, error) {
	return nil, nil
}

func (f *fakeStore) Count(context.Context, string, domain.Filter) (int, error) {
	return 0, nil
}

func (f *fakeStore) CountByKind(
	context.Context, string, domain.Filter,
) (map[string]int, error) {
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
		name        string
		publish     func(*app.Kernel)
		wantKind    string
		wantSession string
		wantWhere   string
		wantIP      string
	}{
		{
			name: "signed in",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SignedIn{
					UserID:    "account-1",
					SessionID: "session-1",
					Device:    "laptop",
					IPAddress: "10.0.0.1",
					At:        happenedAt,
				})
			},
			wantKind: "SignedIn", wantSession: "session-1",
			wantWhere: "laptop", wantIP: "10.0.0.1",
		},
		{
			// One event with an attribute, two kinds. The account module already worked out that this
			// device is new in order to choose its own audit kind; the feed reads the answer rather
			// than asking the question again.
			name: "signed in from a device this account has not used",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SignedIn{
					UserID:    "account-1",
					SessionID: "session-1",
					Device:    "a-new-laptop",
					IPAddress: "10.0.0.1",
					At:        happenedAt,
					NewDevice: true,
				})
			},
			wantKind: "NewDeviceSignedIn", wantSession: "session-1",
			wantWhere: "a-new-laptop", wantIP: "10.0.0.1",
		},
		{
			name: "signed out",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SignedOut{
					UserID: "account-1", SessionID: "session-1", At: happenedAt,
				})
			},
			// No device and no address: the event carries neither, and inventing one would be a row
			// that lies about where the sign-out came from. The session it ended is carried, which is
			// what lets a reader see that a burst of these was one session rather than many.
			wantKind: "SignedOut", wantSession: "session-1", wantWhere: "", wantIP: "",
		},
		{
			// Leaving and being ended are two facts. They arrived as one event until now, so the feed
			// said "signed out" for a revocation that the account page called a revocation.
			name: "one session revoked",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SessionRevoked{
					UserID: "account-1", SessionID: "session-2", At: happenedAt,
				})
			},
			wantKind: "SessionRevoked", wantSession: "session-2", wantWhere: "", wantIP: "",
		},
		{
			// Every session at once names none of them, rather than picking a survivor at random.
			name: "every session revoked at once",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SessionRevoked{
					UserID: "account-1", At: happenedAt,
				})
			},
			wantKind: "SessionRevoked", wantSession: "", wantWhere: "", wantIP: "",
		},
		{
			// The only row in the feed that says another person acted on your account, so it is its
			// own kind rather than an attribute: the client picks a label and an icon by kind, and
			// this is the one that has to look different. It reached the feed not at all until now.
			name: "an administrator revoked somebody's session",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.SessionRevoked{
					UserID: "account-1", SessionID: "session-3", At: happenedAt, ByAdmin: true,
				})
			},
			wantKind: "SessionRevokedByAdmin", wantSession: "session-3",
			wantWhere: "", wantIP: "",
		},
		{
			// The one row whose reader is asking "where did that come from" rather than "was that
			// me", so the device and the address are the point of it. No session: the attempt never
			// got one.
			name: "refused attempt",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.FailedSignIn{
					UserID:    "account-1",
					Device:    "somebody-elses-laptop",
					IPAddress: "203.0.113.42",
					At:        happenedAt,
				})
			},
			wantKind: "FailedSignIn", wantSession: "",
			wantWhere: "somebody-elses-laptop", wantIP: "203.0.113.42",
		},
		{
			name: "password changed",
			publish: func(k *app.Kernel) {
				eventbus.Publish(k.Bus, context.Background(), accountapi.PasswordChanged{
					UserID: "account-1", At: happenedAt,
				})
			},
			// A password belongs to an account rather than to a device, so there is no session here
			// either.
			wantKind: "PasswordChanged", wantSession: "", wantWhere: "", wantIP: "",
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
			if got.SessionID != c.wantSession {
				t.Errorf("SessionID = %q, want %q", got.SessionID, c.wantSession)
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
