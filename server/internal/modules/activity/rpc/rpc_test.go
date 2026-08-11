package rpc

import (
	"context"
	"strings"
	"testing"
	"time"

	"connectrpc.com/connect"

	activityv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/activity/v1"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// recorded is one call to the service, kept so a test can assert what the wire layer decided rather
// than what the caller asked for.
type recorded struct {
	userID    string
	kind      string
	sessionID string
	device    string
	ip        string
	at        time.Time
}

type fakeFeed struct {
	calls []recorded
	err   error
}

func (f *fakeFeed) List(
	context.Context, string, activityapi.Query,
) (activityapi.Page, error) {
	return activityapi.Page{}, nil
}

func (f *fakeFeed) Record(
	_ context.Context, userID, kind, sessionID, device, ip string, at time.Time,
) error {
	f.calls = append(f.calls, recorded{userID, kind, sessionID, device, ip, at})
	return f.err
}

func signedIn(id string) context.Context {
	return auth.WithSubject(context.Background(), auth.Subject{
		ID:        id,
		SessionID: "session-1",
	})
}

// The whole reason a write path is safe to have. A client that could name any kind could write the
// row a reader trusts most into somebody's security feed.
func TestOnlyTheAllowedKindsAreAccepted(t *testing.T) {
	for _, kind := range activityapi.ClientReportableKinds() {
		t.Run("accepts "+kind, func(t *testing.T) {
			feed := &fakeFeed{}
			handler := &handler{svc: feed}

			_, err := handler.RecordClientEvent(signedIn("account-1"),
				connect.NewRequest(&activityv1.RecordClientEventRequest{Kind: kind}))

			if err != nil {
				t.Fatalf("RecordClientEvent returned an error: %v", err)
			}
			if len(feed.calls) != 1 || feed.calls[0].kind != kind {
				t.Errorf("recorded %+v, want one entry of kind %q", feed.calls, kind)
			}
		})
	}

	refused := []struct {
		name string
		kind string
	}{
		{"the row a reader trusts most", activityapi.KindSignedIn},
		{"a security claim", activityapi.KindFailedSignIn},
		{"somebody else's session ending", activityapi.KindSessionRevokedByAdmin},
		{"a kind nobody defined", "AnythingElse"},
		{"nothing at all", ""},
	}
	for _, c := range refused {
		t.Run("refuses "+c.name, func(t *testing.T) {
			feed := &fakeFeed{}
			handler := &handler{svc: feed}

			_, err := handler.RecordClientEvent(signedIn("account-1"),
				connect.NewRequest(&activityv1.RecordClientEventRequest{Kind: c.kind}))

			if errs.KindOf(err) != errs.Invalid {
				t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
			}
			if len(feed.calls) != 0 {
				t.Errorf("recorded %+v, want nothing", feed.calls)
			}
		})
	}
}

// The refusal must not double as a directory of what else to try.
func TestTheRefusalNamesNoKinds(t *testing.T) {
	handler := &handler{svc: &fakeFeed{}}

	_, err := handler.RecordClientEvent(signedIn("account-1"),
		connect.NewRequest(&activityv1.RecordClientEventRequest{Kind: "AnythingElse"}))

	if err == nil {
		t.Fatal("the call was accepted")
	}
	for _, kind := range activityapi.ClientReportableKinds() {
		if strings.Contains(err.Error(), kind) {
			t.Errorf("the refusal message names %q", kind)
		}
	}
}

// Whose feed comes from the token. There is no account id in the request at all, so this asserts the
// only thing left to get wrong: that the subject is the one used.
func TestTheEntryIsFiledUnderTheVerifiedCaller(t *testing.T) {
	feed := &fakeFeed{}
	handler := &handler{svc: feed}

	_, err := handler.RecordClientEvent(signedIn("account-7"),
		connect.NewRequest(&activityv1.RecordClientEventRequest{Kind: activityapi.KindThemeChanged}))

	if err != nil {
		t.Fatalf("RecordClientEvent returned an error: %v", err)
	}
	if feed.calls[0].userID != "account-7" {
		t.Errorf("filed under %q, want the verified caller", feed.calls[0].userID)
	}
}

func TestAnUnverifiedCallerRecordsNothing(t *testing.T) {
	feed := &fakeFeed{}
	handler := &handler{svc: feed}

	_, err := handler.RecordClientEvent(context.Background(),
		connect.NewRequest(&activityv1.RecordClientEventRequest{Kind: activityapi.KindAppUpdated}))

	if errs.KindOf(err) != errs.Unauthenticated {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Unauthenticated)
	}
	if len(feed.calls) != 0 {
		t.Errorf("recorded %+v, want nothing", feed.calls)
	}
}

// An app update happened to an installation rather than to a sign-in, and the server's clock is the
// one that decides when. Both are omissions a caller cannot override, so both are worth pinning.
func TestTheClientDecidesNeitherTheSessionNorTheTime(t *testing.T) {
	feed := &fakeFeed{}
	handler := &handler{svc: feed}
	before := time.Now()

	_, err := handler.RecordClientEvent(signedIn("account-1"),
		connect.NewRequest(&activityv1.RecordClientEventRequest{Kind: activityapi.KindAppUpdated}))

	if err != nil {
		t.Fatalf("RecordClientEvent returned an error: %v", err)
	}

	got := feed.calls[0]
	if got.sessionID != "" {
		// The context carries one, so this is not vacuous: it asserts the handler did not reach for it.
		t.Errorf("sessionID = %q, want empty", got.sessionID)
	}
	if got.at.Before(before) {
		t.Errorf("at = %v, want the server's own clock (at or after %v)", got.at, before)
	}
}

// The device is a claim off the connection, never from the body — there is nowhere in the request to
// put one, and this is what proves the handler reads the header instead.
func TestTheDeviceAndAddressComeOffTheConnection(t *testing.T) {
	feed := &fakeFeed{}
	handler := &handler{svc: feed}

	req := connect.NewRequest(&activityv1.RecordClientEventRequest{
		Kind: activityapi.KindThemeChanged,
	})
	req.Header().Set("User-Agent", "Kakehashi/1.1.2 (Windows NT 10.0; Win64)")
	// Two hops. The first value is the client; everything after it was added by hops we trust less.
	req.Header().Set("X-Forwarded-For", "203.0.113.42, 10.0.0.1")

	if _, err := handler.RecordClientEvent(signedIn("account-1"), req); err != nil {
		t.Fatalf("RecordClientEvent returned an error: %v", err)
	}

	got := feed.calls[0]
	if got.device != "Kakehashi/1.1.2 (Windows NT 10.0; Win64)" {
		t.Errorf("device = %q, want the user agent header", got.device)
	}
	if got.ip != "203.0.113.42" {
		t.Errorf("ip = %q, want the first forwarded value", got.ip)
	}
}
