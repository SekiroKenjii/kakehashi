// Package rpc is the activity module's wire layer.
//
// It is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that. Everything here is mapping, plus the one thing that is genuinely
// the wire's business: the caller's identity arrives on the request context, put there by the
// middleware in internal/app/server, so this is where it is read and where its absence is
// answered. The service below is handed a user id and never learns it was on a network.
//
// One runtime dependency worth saying out loud rather than leaving to be discovered: the mux
// resolves the verifier with TryUse, so this module depends at runtime on some module publishing
// an auth.Verifier — in practice, account — but never at compile time. If account is ever
// unmounted, ListActivity answers UNAUTHENTICATED to everyone instead of failing the build. That
// is correct for a per-account feed in a server with no notion of accounts.
package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	activityv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/activity/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/activity/v1/activityv1connect"
	activityapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// NewRoute builds the Connect handler for ActivityService.
func NewRoute(svc activityapi.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return activityv1connect.NewActivityServiceHandler(&handler{svc: svc}, opts...)
}

// handler adapts activityapi.Service to the generated interface.
type handler struct {
	svc activityapi.Service
}

func (h *handler) ListActivity(
	ctx context.Context, req *connect.Request[activityv1.ListActivityRequest],
) (*connect.Response[activityv1.ListActivityResponse], error) {
	// Whose feed this is comes from the verified token and nowhere else. A Subject on the context
	// was verified — the context key is unforgeable and the middleware is its only writer — so
	// there is no user id in the request and no way to ask for somebody else's. It is the account
	// id and not the session id: scoping by session would break the one thing this module exists
	// to prove, which is that the other machine's sign-in shows up here.
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

	entries, err := h.svc.List(ctx, subject.ID, int(req.Msg.GetPageSize()))
	if err != nil {
		return nil, err
	}

	out := make([]*activityv1.Entry, len(entries))
	for i, e := range entries {
		out[i] = toProto(e)
	}
	return connect.NewResponse(&activityv1.ListActivityResponse{Entries: out}), nil
}

func toProto(e activityapi.Entry) *activityv1.Entry {
	return &activityv1.Entry{
		Kind:       e.Kind,
		Device:     e.Device,
		IpAddress:  e.IPAddress,
		OccurredAt: timestamppb.New(e.OccurredAt),
	}
}

var _ activityv1connect.ActivityServiceHandler = (*handler)(nil)
