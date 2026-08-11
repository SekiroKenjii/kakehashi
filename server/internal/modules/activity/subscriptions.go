package activity

import (
	"context"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
)

// The module's entire foreign surface: this is the only file under internal/modules/activity/ that
// imports another module, and the only one anyone edits when the feed's scope changes.
//
// It is separate from module.go because the root package now has two units and both halves of the
// counter-rule hold. "What does the feed show?" is asked far more often than "how is this mounted",
// and the answer to the first should not be buried past Routes().
//
// Notes is deliberately absent. notesapi's events carry no actor, so putting note edits in a
// per-account feed would mean either reading the caller off the ambient publisher context — a
// dependency on the notes module's calling convention that no gate checks, that loses attribution
// the first time a handler follows the eventbus doc's advice to spawn a goroutine, and that
// silently records nothing for an anonymous create — or an edit to notesapi for this module's
// benefit. Neither belongs here. When notes gains an owner, the actor becomes a field the compiler
// checks, and three more closures land in this file.

// subscribe registers this module's interest in the account module's facts.
//
// From Register rather than Start, for two reasons. Subscriptions are permanent — there is no
// Unsubscribe — so they must be made once, at mount, and never from a request handler. And Register
// is legal despite its ban on resolving other modules, because Subscribe resolves nothing: it
// touches only the bus, which the kernel builds before any module registers. Start would work and
// would be worse, because the earlier the subscription exists the smaller the window in which a
// published fact lands on an empty room.
func (m *Module) subscribe(k *app.Kernel) {
	// One line per kind, and this mapping is the whole vocabulary translation — the reason
	// activityapi declares its own constants instead of re-exporting the account module's. It is
	// also the place to ask "should the user see this?": the account module publishes IP addresses
	// and device strings, and a feed that grew rows automatically because someone in another
	// module added a struct would be a privacy bug waiting to happen.
	//
	// The NewDevice flag rides the event. What the seam forbids is a direction of dependency —
	// activity calling into account, or account naming activity — not which fields an event
	// carries: the account module computes NewDevice to choose its own audit kind whether anybody
	// listens or not, and publishing a conclusion it already reached binds it to no one. The test
	// is not "who benefits", it is "who now has to know about whom", and the answer here is nobody.
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
// The swallow is mandatory rather than chosen: the bus's handler signature gives an error nowhere
// to go. That happens to be the judgement the account module already writes down about its own
// audit trail — failing to record must not fail the sign-in that caused it — and here the
// architecture makes it structural. Publish ignores whatever a handler does and recovers a panic,
// so a dead Mongo cannot fail a sign-in even in principle.
func (m *Module) record(
	ctx context.Context, userID, kind, sessionID, device, ip string, at time.Time,
) {
	if err := m.svc.Record(ctx, userID, kind, sessionID, device, ip, at); err != nil {
		m.log.ErrorContext(ctx, "activity entry not recorded", "kind", kind, "error", err)
	}
}
