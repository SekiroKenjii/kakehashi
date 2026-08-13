// Package rpc holds the server's Connect wiring: the options every module's handler is built with,
// and the interceptor that turns domain errors into wire errors. Translation happens in exactly
// one place, so every handler maps the same error kind to the same status code.
package rpc

import (
	"context"
	"errors"
	"log/slog"

	"connectrpc.com/connect"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// HandlerOptions are the options every Connect handler in this server is built with.
//
//	path, handler := healthv1connect.NewHealthServiceHandler(svc, k.RPC...)
func HandlerOptions(log *slog.Logger) []connect.HandlerOption {
	return []connect.HandlerOption{
		connect.WithInterceptors(errorInterceptor(log)),
	}
}

// errorInterceptor maps an errs.Kind onto a Connect status code, logs the ones nobody expected,
// and replaces the message with something safe to send.
//
// Handlers therefore return plain errors from their service layer and never construct a
// *connect.Error themselves. That is the point: a service is not supposed to know it is being
// called over a network, and the moment it starts choosing status codes, it does.
func errorInterceptor(log *slog.Logger) connect.Interceptor {
	return connect.UnaryInterceptorFunc(func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			res, err := next(ctx, req)
			if err == nil {
				return res, nil
			}

			// Already shaped for the wire, by this interceptor on a nested call or by Connect
			// itself (a codec failure, a request that exceeded the size limit). Leave it alone.
			var alreadyWire *connect.Error
			if errors.As(err, &alreadyWire) {
				return nil, err
			}

			kind := errs.KindOf(err)
			if kind == errs.Internal {
				// The only kind worth a log line. The rest are the caller's mistakes, and logging
				// those at error level trains everyone to ignore the error log.
				log.ErrorContext(ctx, "rpc failed",
					"procedure", req.Spec().Procedure,
					"error", err,
				)
			}

			return nil, connect.NewError(codeFor(kind), errors.New(errs.PublicMessage(err)))
		}
	})
}

func codeFor(kind errs.Kind) connect.Code {
	switch kind {
	case errs.NotFound:
		return connect.CodeNotFound
	case errs.Invalid:
		return connect.CodeInvalidArgument
	case errs.Conflict:
		return connect.CodeAlreadyExists
	case errs.Unauthenticated:
		return connect.CodeUnauthenticated
	case errs.Forbidden:
		return connect.CodePermissionDenied
	default:
		return connect.CodeInternal
	}
}
