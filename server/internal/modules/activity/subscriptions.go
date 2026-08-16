package activity

import (
	"context"
	"time"

	"__GO_MODULE__/server/internal/app"
	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	activityapi "__GO_MODULE__/server/internal/modules/activity/api"
)

// The module's entire foreign surface: this is the only file under internal/modules/activity/ that
// imports another module, and the only one anyone edits when the feed's scope changes.
//
// Notes is deliberately absent. notesapi's events carry no actor, so putting note edits in a
// per-account feed would mean reading the caller off the ambient publisher context — a convention
// no gate checks, and one that loses attribution the moment a handler defers to a goroutine — or
// editing notesapi for this module's benefit. When a note event carries an actor field, its
// closures land in this file.

// subscribe registers this module's interest in the account module's facts.
//
// From Register rather than Start. Subscriptions are permanent — there is no Unsubscribe — so
// they are made once, at mount, never from a request handler. Register's ban on resolving other
// modules is not violated: Subscribe touches only the bus, which the kernel builds before any
// module registers. And the earlier the subscription exists, the smaller the window in which a
// published fact finds no listener.
func (m *Module) subscribe(k *app.Kernel) {
	// The whole vocabulary translation, and the gate on what a user sees: no row reaches the feed
	// without a line here choosing to write it.
	app.Subscribe(k, func(ctx context.Context, e accountapi.SignedIn) {
		kind := activityapi.KindSignedIn
		if e.NewDevice {
			kind = activityapi.KindNewDeviceSignedIn
		}
		m.record(ctx, e.UserID, kind, e.SessionID, e.Device, e.IPAddress, e.At)
	})

	// Leaving and being ended arrive as two events and become two rows, with ByAdmin telling
	// somebody else's revocation apart: docs/adr/0003-signedout-vs-sessionrevoked.md
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

// record writes one entry, logging and dropping a failure.
//
// The swallow is structural: the bus's handler signature gives an error nowhere to go, and
// failing to record must not fail the sign-in that caused it. Publish ignores whatever a handler
// does and recovers a panic, so a dead Mongo cannot fail a sign-in even in principle.
func (m *Module) record(
	ctx context.Context, userID, kind, sessionID, device, ip string, at time.Time,
) {
	if err := m.svc.Record(ctx, userID, kind, sessionID, device, ip, at); err != nil {
		m.log.ErrorContext(ctx, "activity entry not recorded", "kind", kind, "error", err)
	}
}
