package activity

import (
	"context"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
)

// The module's entire foreign surface: the only file under internal/modules/activity/ that imports
// another module, and the only one to edit when the feed's scope changes.
//
// Notes is deliberately absent. notesapi's events carry no actor, so a note edit could only be
// attributed by reading the caller off the ambient publisher context — which no gate checks,
// which loses attribution the first time a handler spawns a goroutine, and which records nothing
// for an anonymous create — or by editing notesapi for this module's benefit. When notes gains an
// owner, the actor becomes a field the compiler checks and three more closures land here.

// From Register rather than Start. Subscriptions are permanent — there is no Unsubscribe — so
// they must be made once, at mount, and the earlier they exist the smaller the window in which a
// published fact lands on an empty room. Legal despite Register's ban on resolving other modules,
// because Subscribe resolves nothing: it touches only the bus, which the kernel builds before any
// module registers.
func (m *Module) subscribe(k *app.Kernel) {
	// One line per kind, and this mapping is the whole vocabulary translation — the reason
	// activityapi declares its own constants instead of re-exporting the account module's. It is
	// also the place to ask "should the user see this?": the account module publishes IP addresses
	// and device strings, and a feed that grew rows because someone in another module added a
	// struct would be a privacy bug waiting to happen.
	//
	// NewDevice riding on the event is not the inverted dependency this seam avoids. What the seam
	// forbids is a direction — activity calling into account, or account naming activity — and the
	// account module computes NewDevice to choose its own audit kind whether anybody listens or
	// not. The test is "who now has to know about whom", not "who benefits".
	app.Subscribe(k, func(ctx context.Context, e accountapi.SignedIn) {
		kind := activityapi.KindSignedIn
		if e.NewDevice {
			kind = activityapi.KindNewDeviceSignedIn
		}
		m.record(ctx, e.UserID, kind, e.SessionID, e.Device, e.IPAddress, e.At)
	})

	// Leaving and being ended are two events and two rows. As one event, the feed said "signed out"
	// for a revocation the account page called a revocation.
	app.Subscribe(k, func(ctx context.Context, e accountapi.SignedOut) {
		m.record(ctx, e.UserID, activityapi.KindSignedOut, e.SessionID, "", "", e.At)
	})
	app.Subscribe(k, func(ctx context.Context, e accountapi.SessionRevoked) {
		kind := activityapi.KindSessionRevoked
		if e.ByAdmin {
			kind = activityapi.KindSessionRevokedByAdmin
		}
		m.record(ctx, e.UserID, kind, e.SessionID, "", "", e.At)
	})

	// The device and address are the point of this row: a refused attempt is the one entry whose
	// reader is asking "where did that come from", not "was that me".
	app.Subscribe(k, func(ctx context.Context, e accountapi.FailedSignIn) {
		m.record(ctx, e.UserID, activityapi.KindFailedSignIn, "", e.Device, e.IPAddress, e.At)
	})
	app.Subscribe(k, func(ctx context.Context, e accountapi.PasswordChanged) {
		m.record(ctx, e.UserID, activityapi.KindPasswordChanged, "", "", "", e.At)
	})
}

// The swallow is mandatory rather than chosen: the bus's handler signature gives an error nowhere
// to go. Publish ignores whatever a handler does and recovers a panic, so a dead Mongo cannot fail
// the sign-in that caused the write.
func (m *Module) record(
	ctx context.Context, userID, kind, sessionID, device, ip string, at time.Time,
) {
	if err := m.svc.Record(ctx, userID, kind, sessionID, device, ip, at); err != nil {
		m.log.ErrorContext(ctx, "activity entry not recorded", "kind", kind, "error", err)
	}
}
