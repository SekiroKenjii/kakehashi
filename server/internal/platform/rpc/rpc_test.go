package rpc

import (
	"context"
	"io"
	"log/slog"
	"net/http"
	"testing"

	"connectrpc.com/connect"

	"__GO_MODULE__/server/internal/platform/errs"
)

// stubConn is the least a StreamingHandlerConn can be. Only Spec is read.
type stubConn struct{}

func (stubConn) Spec() connect.Spec {
	return connect.Spec{Procedure: "/plugins.v1.PluginService/DownloadPluginVersion"}
}

func (stubConn) Peer() connect.Peer           { return connect.Peer{} }
func (stubConn) Receive(any) error            { return io.EOF }
func (stubConn) RequestHeader() http.Header   { return http.Header{} }
func (stubConn) Send(any) error               { return nil }
func (stubConn) ResponseHeader() http.Header  { return http.Header{} }
func (stubConn) ResponseTrailer() http.Header { return http.Header{} }

func discard() *slog.Logger {
	return slog.New(slog.NewTextHandler(io.Discard, nil))
}

// The download and the publish are this server's only streaming procedures, and both are the
// plugins module's. Before the interceptor was a full connect.Interceptor they answered with the
// service's own message and an unknown code, because UnaryInterceptorFunc.WrapStreamingHandler is
// documented as a no-op.
func TestWrapStreamingHandlerMapsTheKind(t *testing.T) {
	t.Parallel()

	cases := map[string]struct {
		err  error
		code connect.Code
	}{
		"not found": {errs.NotFoundf("no such version"), connect.CodeNotFound},
		"forbidden": {errs.Forbiddenf("not yours"), connect.CodePermissionDenied},
		"internal":  {errs.Internalf(nil, "the disk is on fire"), connect.CodeInternal},
	}

	for name, want := range cases {
		t.Run(name, func(t *testing.T) {
			t.Parallel()

			handler := errorInterceptor{log: discard()}.WrapStreamingHandler(
				func(context.Context, connect.StreamingHandlerConn) error { return want.err },
			)

			err := handler(context.Background(), stubConn{})

			if got := connect.CodeOf(err); got != want.code {
				t.Fatalf("code = %v, want %v", got, want.code)
			}
		})
	}
}

// An Internal message names something only the server should know, so what reaches the wire is the
// public one — the same rule the unary half already followed.
func TestWrapStreamingHandlerHidesAnInternalMessage(t *testing.T) {
	t.Parallel()

	secret := "connection string: sa/hunter2"
	handler := errorInterceptor{log: discard()}.WrapStreamingHandler(
		func(context.Context, connect.StreamingHandlerConn) error {
			return errs.Internalf(nil, "%s", secret)
		},
	)

	err := handler(context.Background(), stubConn{})

	if err == nil {
		t.Fatal("err = nil, want an error")
	}
	if got := err.Error(); got == secret {
		t.Fatalf("message = %q, want the public one", got)
	}
}

func TestWrapStreamingHandlerPassesSuccessThrough(t *testing.T) {
	t.Parallel()

	handler := errorInterceptor{log: discard()}.WrapStreamingHandler(
		func(context.Context, connect.StreamingHandlerConn) error { return nil },
	)

	if err := handler(context.Background(), stubConn{}); err != nil {
		t.Fatalf("err = %v, want nil", err)
	}
}
