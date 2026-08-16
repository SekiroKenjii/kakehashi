// Package rpc is the health module's wire layer.
//
// It is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that. The reason is the same one that keeps api.Status separate from a
// domain entity: generated types are the wire's shape, not the module's, and once a service starts
// returning them, a change to the schema becomes a change to the service.
//
// Everything here is mapping. No decisions, no rules — those live in service/.
package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	healthv1 "__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/health/v1"
	"__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/health/v1/healthv1connect"
	healthapi "__GO_MODULE__/server/internal/modules/health/api"
)

// NewRoute builds the Connect handler for HealthService.
//
// It returns the pattern and handler exactly as Connect produces them, which is the pair
// app.Route holds, so the module wires it up without touching anything generated.
func NewRoute(
	svc healthapi.Service, opts []connect.HandlerOption,
) (string, http.Handler) {
	return healthv1connect.NewHealthServiceHandler(&handler{svc: svc}, opts...)
}

// NewLiveness builds the plain-HTTP liveness probe.
//
// It exists alongside the RPC because the things that check liveness — a container runtime, a load
// balancer, a uptime monitor — speak HTTP and nothing else. Asking them to frame a Connect request
// is not a fight worth having over a two-hundred-byte answer.
func NewLiveness() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ok\n"))
	})
}

// handler adapts healthapi.Service to the generated interface.
type handler struct {
	svc healthapi.Service
}

func (h *handler) Ping(
	ctx context.Context, req *connect.Request[healthv1.PingRequest],
) (*connect.Response[healthv1.PingResponse], error) {
	status, err := h.svc.Ping(ctx, req.Msg.GetMessage())
	if err != nil {
		// Returned bare: the interceptor in platform/rpc decides the status code and what the caller
		// may read, which every handler would otherwise decide differently.
		return nil, err
	}

	return connect.NewResponse(&healthv1.PingResponse{
		Message:    status.Message,
		ServerTime: timestamppb.New(status.ServerTime),
	}), nil
}

var _ healthv1connect.HealthServiceHandler = (*handler)(nil)
