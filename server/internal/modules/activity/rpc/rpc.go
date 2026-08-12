// Package rpc is the activity module's wire layer.
//
// It is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that. Everything here is mapping, plus the one thing that is genuinely
// the wire's business: the caller's identity arrives on the request context, put there by the
// middleware in internal/app/server, so this is where it is read and where its absence is answered.
// The service below is handed a user id and never learns it was on a network.
//
// The mux resolves the verifier with TryUse, so this module depends at runtime on some module
// publishing an auth.Verifier — in practice, account — but never at compile time. If account is
// ever unmounted, ListActivity answers UNAUTHENTICATED to everyone instead of failing the build,
// which is correct for a per-account feed in a server with no notion of accounts.
package rpc

import (
	"context"
	"net"
	"net/http"
	"sort"
	"strings"
	"time"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	activityv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/activity/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/activity/v1/activityv1connect"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Declared here rather than taken from activityapi, which is deliberately read-only: another module
// must not be able to append to somebody's history, but the module's own wire layer is not another
// module. Widening activityapi.Service to make this compile would hand the write to every module in
// the server to solve a problem inside one of them.
type feed interface {
	List(ctx context.Context, userID string, q activityapi.Query) (activityapi.Page, error)
	Record(
		ctx context.Context, userID, kind, sessionID, device, ip string, at time.Time,
	) error
}

func NewRoute(svc feed, opts []connect.HandlerOption) (string, http.Handler) {
	return activityv1connect.NewActivityServiceHandler(&handler{svc: svc}, opts...)
}

type handler struct {
	svc feed
}

// Four things the request does not get to decide, which together are what make the write safe:
// whose feed (the token), when (the server's clock), where from (the connection), and what kind of
// fact (a closed list). All that is left for the caller is which of two things happened.
func (h *handler) RecordClientEvent(
	ctx context.Context, req *connect.Request[activityv1.RecordClientEventRequest],
) (*connect.Response[activityv1.RecordClientEventResponse], error) {
	subject, ok := auth.SubjectFrom(ctx)
	if !ok {
		return nil, errs.Unauthenticatedf("Sign in to record activity.")
	}

	kind := req.Msg.GetKind()
	if !activityapi.CanReport(kind) {
		// The message names no kinds: a refusal that listed what is allowed would teach a caller
		// what else to try, and the client already knows — it sends one of two constants it was
		// compiled with.
		return nil, errs.Invalidf("That is not something a client may record.")
	}

	device, ip := callerFacts(req)

	// No session id. These facts belong to an installation rather than to a sign-in — the app was
	// updated before anybody authenticated — and naming whichever session happened to be open would
	// imply the two were connected.
	//
	// The time is the server's. A client with a wrong clock would scatter rows through the history,
	// and one with a bad intention could slot a row between two security events and change what the
	// sequence appears to say.
	if err := h.svc.Record(ctx, subject.ID, kind, "", device, ip, time.Now()); err != nil {
		return nil, err
	}
	return connect.NewResponse(&activityv1.RecordClientEventResponse{}), nil
}

// Both are claims rather than facts — a user agent lies freely and an address may be a proxy —
// which is why they are only ever displayed and never used for a decision.
//
// It duplicates the account module's edge helper of the same name, and that is the module boundary
// working rather than failing: that one reads a *http.Request, this one a Connect request, and
// neither module may import the other. If a second Connect handler ever needs this, it belongs in
// platform/rpc — one caller is not yet a reason to put it there.
func callerFacts(req connect.AnyRequest) (device, ip string) {
	device = strings.TrimSpace(req.Header().Get("User-Agent"))
	if len(device) > 256 {
		device = device[:256]
	}

	// Behind the reverse proxy the peer address is the proxy's; the original is in the header it
	// appends. First value wins: everything after it was added by hops we trust less.
	if forwarded := req.Header().Get("X-Forwarded-For"); forwarded != "" {
		return device, strings.TrimSpace(strings.Split(forwarded, ",")[0])
	}
	if host, _, err := net.SplitHostPort(req.Peer().Addr); err == nil {
		return device, host
	}
	return device, req.Peer().Addr
}

func (h *handler) ListActivity(
	ctx context.Context, req *connect.Request[activityv1.ListActivityRequest],
) (*connect.Response[activityv1.ListActivityResponse], error) {
	// Whose feed this is comes from the verified token and nowhere else — the context key is
	// unforgeable and the middleware is its only writer — so there is no user id in the request and
	// no way to ask for somebody else's. The account id and not the session id: scoping by session
	// would break the one thing this module exists to prove, that the other machine's sign-in shows
	// up here.
	subject, ok := auth.SubjectFrom(ctx)
	if !ok {
		// Not an empty list. An empty feed and an expired token are the same picture on screen and
		// opposite facts, and collapsing them produces a client that silently shows nothing.
		//
		// The check lives here rather than in service/ because identity is transport-borne and is
		// unpacked at the transport edge. A service that reaches into a request context for its
		// caller has started knowing it is on a network.
		return nil, errs.Unauthenticatedf("Sign in to see your activity.")
	}

	page, err := h.svc.List(ctx, subject.ID, activityapi.Query{
		From:      asTime(req.Msg.GetFrom()),
		To:        asTime(req.Msg.GetTo()),
		Category:  req.Msg.GetCategory(),
		Search:    req.Msg.GetQuery(),
		PageToken: req.Msg.GetPageToken(),
		PageSize:  int(req.Msg.GetPageSize()),
	})
	if err != nil {
		return nil, err
	}

	entries := make([]*activityv1.Entry, len(page.Entries))
	for i, e := range page.Entries {
		entries[i] = toProto(e)
	}

	return connect.NewResponse(&activityv1.ListActivityResponse{
		Entries:       entries,
		NextPageToken: page.NextPageToken,
		TotalCount:    int32(page.Total),
		Counts:        toCounts(page.Counts),
		KindCounts:    toKindCounts(page.KindCounts),
		RetentionDays: int32(page.RetentionDays),
	}), nil
}

// An unset timestamp becomes the zero time, which every layer below reads as "unbounded on that
// side" — so a client sending only `from` needs no sentinel value.
func asTime(ts *timestamppb.Timestamp) time.Time {
	if ts == nil {
		return time.Time{}
	}
	return ts.AsTime()
}

// Ordered for the same reason toCounts is.
func toKindCounts(counts map[string]int) []*activityv1.KindCount {
	if len(counts) == 0 {
		return nil
	}

	kinds := make([]string, 0, len(counts))
	for kind := range counts {
		kinds = append(kinds, kind)
	}
	sort.Strings(kinds)

	out := make([]*activityv1.KindCount, len(kinds))
	for i, kind := range kinds {
		out[i] = &activityv1.KindCount{Kind: kind, Count: int32(counts[kind])}
	}
	return out
}

// The service answers with a map; the wire needs an order, because a chip row that reshuffles
// between refreshes looks broken. Sorting by name is arbitrary but stable, which is the whole
// requirement.
func toCounts(counts map[string]int) []*activityv1.CategoryCount {
	if len(counts) == 0 {
		return nil
	}

	categories := make([]string, 0, len(counts))
	for category := range counts {
		categories = append(categories, category)
	}
	sort.Strings(categories)

	out := make([]*activityv1.CategoryCount, len(categories))
	for i, category := range categories {
		out[i] = &activityv1.CategoryCount{
			Category: category,
			Count:    int32(counts[category]),
		}
	}
	return out
}

func toProto(e activityapi.Entry) *activityv1.Entry {
	return &activityv1.Entry{
		Id:         e.ID,
		Kind:       e.Kind,
		Category:   e.Category,
		SessionId:  e.SessionID,
		Device:     e.Device,
		Platform:   e.Platform,
		IpAddress:  e.IPAddress,
		OccurredAt: timestamppb.New(e.OccurredAt),
	}
}

var _ activityv1connect.ActivityServiceHandler = (*handler)(nil)
