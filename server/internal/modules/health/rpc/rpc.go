// Package rpc is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that: generated types are the wire's shape, not the module's, and once a
// service returns them a schema change becomes a service change. Mapping only — rules live in
// service/.
package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	healthv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/health/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/health/v1/healthv1connect"
	healthapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/health/api"
)

func NewRoute(
	svc healthapi.Service, opts []connect.HandlerOption,
) (string, http.Handler) {
	return healthv1connect.NewHealthServiceHandler(&handler{svc: svc}, opts...)
}

// NewLiveness exists alongside the RPC because the things that check liveness — a container
// runtime, a load balancer, an uptime monitor — speak plain HTTP and cannot frame a Connect
// request.
func NewLiveness() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ok\n"))
	})
}

type handler struct {
	svc healthapi.Service
}

func (h *handler) Ping(
	ctx context.Context, req *connect.Request[healthv1.PingRequest],
) (*connect.Response[healthv1.PingResponse], error) {
	status, err := h.svc.Ping(ctx, req.Msg.GetMessage())
	if err != nil {
		// Bare: the interceptor in platform/rpc decides the status code and what the caller is
		// allowed to read.
		return nil, err
	}

	return connect.NewResponse(&healthv1.PingResponse{
		Message:    status.Message,
		ServerTime: timestamppb.New(status.ServerTime),
	}), nil
}

var _ healthv1connect.HealthServiceHandler = (*handler)(nil)
