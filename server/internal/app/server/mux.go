// Package server turns the mounted modules into one HTTP surface. It knows that modules
// contribute routes, and nothing about what any of them do.
package server

import (
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"strings"
	"sync"
	"time"

	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"golang.org/x/net/http2"
	"golang.org/x/net/http2/h2c"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// New builds the server's handler from the kernel's routes.
//
// Connect services and OIDC endpoints share one net/http mux; net/http resolves between them by
// path specificity, so a catch-all at "/" and a service at "/kakehashi.notes.v1.NotesService/"
// coexist.
//
// Every route carries its own policy and this enforces it: the kernel refuses at boot a route
// with no policy, and one that checks no permission unless the composition root named its module.
// Why per route, not per module: docs/adr/0001-per-route-permission-policy.md.
func New(k *app.Kernel) *Server {
	// On the http2.Server, not the http.Server: h2c hijacks the connection out of net/http's
	// tracking on its first byte, so http.Server's own timeouts never reach it.
	srv := &Server{
		kernel: k,
		h2s: &http2.Server{
			IdleTimeout: 120 * time.Second,

			// A ping every 30 seconds with a 10-second answer window, so a connection whose peer
			// vanished is reaped instead of held open by a socket nobody is on the other end of.
			ReadIdleTimeout: 30 * time.Second,
			PingTimeout:     10 * time.Second,
		},
	}

	mux := http.NewServeMux()

	// Resolved from the registry rather than by importing a module. With nothing registered every
	// module is reachable — a server with no access-control module mounted.
	permissions, gating := app.TryUse[auth.Permissions](k)
	if !gating {
		k.Log.Warn("no authorization module is mounted; every module is open to any caller")
	}

	for _, route := range k.Routes() {
		handler := route.Handler
		if key := route.Policy.PermissionFor(route.Module); gating && key != "" {
			handler = requirePermission(key, handler)
		} else if route.Policy.Kind() == app.PolicySignedIn {
			handler = requireSignedIn(handler)
		}

		// Duplicate patterns panic here: two modules claiming the same path is a design mistake,
		// surfaced at boot.
		mux.Handle(route.Pattern, handler)
		k.Log.Debug("route mounted",
			"pattern", route.Pattern, "module", route.Module, "policy", route.Policy.String())
	}

	// Outside-in: recoverPanics -> otelhttp -> logRequests -> mux. Logging sits inside otelhttp so
	// r.Context() carries the span, and a log line can be matched to its trace.
	var handler http.Handler = mux
	// Resolution runs on every route, not only gated ones:
	// docs/adr/0001-per-route-permission-policy.md.
	if gating {
		handler = resolvePermissions(k.Log, permissions, handler)
	}
	if verifier, ok := app.TryUse[auth.Verifier](k); ok {
		handler = authenticate(verifier, handler)
	}
	handler = logRequests(k.Log, handler)
	handler = otelhttp.NewHandler(handler, "kakehashi",
		// Without this every span is named after the handler pattern, so all of Connect's traffic
		// collapses into one span name and the traces stop distinguishing procedures.
		otelhttp.WithSpanNameFormatter(func(_ string, r *http.Request) string {
			return r.Method + " " + r.URL.Path
		}),
	)
	handler = recoverPanics(k.Log, handler)

	// Outermost of the ordinary chain, so every request the server accepts is counted — including
	// the ones that 404 or panic. Shutdown waits on this before anything closes a database pool.
	handler = srv.track(handler)

	// gRPC needs HTTP/2 and net/http negotiates it only over TLS; this server speaks cleartext
	// behind a TLS-terminating proxy, so it serves h2c. Transport trust is settled in front of it.
	srv.handler = h2c.NewHandler(handler, srv.h2s)

	return srv
}

// Server is the mounted surface plus the two things shutting it down cleanly needs: the HTTP/2
// server that h2c hands its connections to, and a count of the requests still running.
type Server struct {
	kernel  *app.Kernel
	handler http.Handler
	h2s     *http2.Server

	// inFlight counts requests between accept and response. Shutdown is not allowed to finish, and
	// the stores are not allowed to close, while it is above zero.
	inFlight sync.WaitGroup
}

// Handler is the whole surface, for a test that drives it through httptest rather than a port.
func (s *Server) Handler() http.Handler { return s.handler }

// Run serves until ctx is cancelled, then stops taking requests and waits for the ones in flight.
//
// h2c hijacks every HTTP/2 connection out of net/http's tracking, so http.Server.Shutdown has
// nothing to drain and returns in milliseconds; without the WaitGroup drain, the caller would
// close the SQL and Mongo pools underneath handlers still running. ConfigureServer installs the
// HTTP/2 graceful shutdown hook so peers get a GOAWAY.
func (s *Server) Run(ctx context.Context) error {
	httpServer := &http.Server{
		Addr:    s.kernel.Cfg.Addr,
		Handler: s.handler,

		// Headers only. A blanket ReadTimeout would cap the body too, silently breaking any
		// client-streaming RPC somebody adds later.
		ReadHeaderTimeout: 10 * time.Second,
		IdleTimeout:       120 * time.Second,
	}

	// Registers the HTTP/2 graceful-shutdown hook on httpServer. Without it, h2s's settings are
	// never consulted at shutdown.
	if err := http2.ConfigureServer(httpServer, s.h2s); err != nil {
		return err
	}

	serveErr := make(chan error, 1)
	go func() { serveErr <- httpServer.ListenAndServe() }()

	select {
	case err := <-serveErr:
		if errors.Is(err, http.ErrServerClosed) {
			return nil
		}
		return err
	case <-ctx.Done():
		s.kernel.Log.InfoContext(ctx, "shutdown signal received")
	}

	// Detached from ctx, which is already cancelled: handing the cleanup the very context whose
	// cancellation started it makes every step fail immediately.
	stopCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), s.kernel.Cfg.ShutdownTimeout)
	defer cancel()

	return errors.Join(httpServer.Shutdown(stopCtx), s.drain(stopCtx))
}

// drain waits for the requests still running, or reports that it gave up on them.
//
// Giving up is reported rather than swallowed: the caller is about to close the stores those
// requests are using, and the report explains any client errors the shutdown produces.
func (s *Server) drain(ctx context.Context) error {
	done := make(chan struct{})
	go func() {
		s.inFlight.Wait()
		close(done)
	}()

	select {
	case <-done:
		return nil
	case <-ctx.Done():
		return errors.New("gave up waiting for in-flight requests; the stores closed underneath them")
	}
}

// track counts a request for the whole time it is being served.
func (s *Server) track(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		s.inFlight.Add(1)
		defer s.inFlight.Done()
		next.ServeHTTP(w, r)
	})
}

// authenticate turns a Bearer token into a Subject on the request context.
//
// It never rejects. An invalid token and no token both pass through as an anonymous request,
// because only the endpoint knows whether anonymity is acceptable there — the health probe says
// yes, the account endpoints say 401. What the middleware guarantees is narrower and more useful:
// if a Subject is on the context, it was verified.
func authenticate(verifier auth.Verifier, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		const prefix = "Bearer "

		header := r.Header.Get("Authorization")
		if len(header) > len(prefix) && strings.EqualFold(header[:len(prefix)], prefix) {
			if subject, err := verifier.Verify(r.Context(), header[len(prefix):]); err == nil {
				r = r.WithContext(auth.WithSubject(r.Context(), subject))
			}
		}
		next.ServeHTTP(w, r)
	})
}

// resolvePermissions works out what the caller may do, once, and puts it on the context.
//
// Anonymous requests pass through untouched: there is nothing to resolve, and refusing here would
// break the endpoints whose whole job is to serve someone who has not signed in yet.
func resolvePermissions(
	log *slog.Logger, permissions auth.Permissions, next http.Handler,
) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		subject, ok := auth.SubjectFrom(r.Context())
		if !ok {
			next.ServeHTTP(w, r)
			return
		}

		grants, err := permissions.Resolve(r.Context(), subject)
		if err != nil {
			// Fail closed: an unreachable policy store must not silently open every gated
			// module. Signing in is unaffected: it is an anonymous request and returned above.
			log.ErrorContext(r.Context(), "permissions could not be resolved", "error", err)
			http.Error(w, "access could not be checked", http.StatusServiceUnavailable)
			return
		}

		next.ServeHTTP(w, r.WithContext(auth.WithGrants(r.Context(), grants)))
	})
}

// requireSignedIn refuses a request with no verified caller.
//
// The policy for endpoints that are about the caller's own account or the caller's own view. It
// checks no permission, which is the point: a permission guarding your own profile is a permission
// somebody could take away, leaving an account that can sign in and then do nothing.
//
// The handlers behind these routes still read the Subject and still refuse without one — they need
// the value, not just the assurance, and they answer in their own protocol's error shape. This is
// the outer guarantee, not a replacement for the inner one.
func requireSignedIn(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if _, ok := auth.SubjectFrom(r.Context()); !ok {
			deny(w, http.StatusUnauthorized, "unauthenticated",
				"This endpoint requires a signed-in caller.")
			return
		}
		next.ServeHTTP(w, r)
	})
}

// requirePermission refuses a request whose caller does not hold the permission the route named.
//
// This is the enforcement point of the whole access model: one wrapper per route rather than a
// check per handler, so a route is checked because it declared a policy, not because its author
// remembered to wrap it.
//
// It sits inside authenticate, so the Subject is already on the context, and inside logRequests,
// so every refusal is one ordinary request line with its 403.
func requirePermission(permission string, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if _, ok := auth.SubjectFrom(r.Context()); !ok {
			// Refused, not passed through: omitting the header must not meet fewer checks than
			// sending a token.
			deny(w, http.StatusUnauthorized, "unauthenticated",
				"This endpoint requires a signed-in caller.")
			return
		}

		if !auth.GrantsFrom(r.Context()).Allows(permission) {
			// 403 naming the permission, not 404: the endpoint is compiled into the client anyway,
			// and the name is what lets it say which grant to ask for.
			deny(w, http.StatusForbidden, "forbidden",
				"This endpoint requires the "+permission+" permission.")
			return
		}

		next.ServeHTTP(w, r)
	})
}

// deny writes the refusal in the error envelope this server's REST surface is pinned to.
//
// {"error","message"} is what docs/CONTRACTS.md documents and what the client's gateway parses;
// changing the shape breaks deployed clients. A Connect client cannot read a body this middleware
// writes — it runs before Connect sees the request — but the REST endpoints under /account can.
func deny(w http.ResponseWriter, status int, code, message string) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]string{"error": code, "message": message})
}

// recoverPanics keeps one bad request from taking down the process.
//
// net/http already recovers per connection, but it kills the connection and logs without request
// context. This turns the panic into a 500 and a log line with the request attached.
func recoverPanics(log *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		defer func() {
			if v := recover(); v != nil {
				log.ErrorContext(r.Context(), "panic serving request",
					"method", r.Method,
					"path", r.URL.Path,
					"panic", v,
				)
				// Best-effort: if the handler already wrote a header this is a no-op, and Go logs
				// the superfluous call. Nothing better is available once bytes are on the wire.
				w.WriteHeader(http.StatusInternalServerError)
			}
		}()
		next.ServeHTTP(w, r)
	})
}

// logRequests records one line per request, after it finishes.
func logRequests(log *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		started := time.Now()
		rec := &statusRecorder{ResponseWriter: w, status: http.StatusOK}

		next.ServeHTTP(rec, r)

		log.InfoContext(r.Context(), "request",
			"method", r.Method,
			"path", r.URL.Path,
			"status", rec.status,
			"duration_ms", time.Since(started).Milliseconds(),
		)
	})
}

// statusRecorder remembers the status code so the log line can report it.
type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (r *statusRecorder) WriteHeader(status int) {
	r.status = status
	r.ResponseWriter.WriteHeader(status)
}

// Flush forwards to the wrapped writer.
//
// Embedding a ResponseWriter in a struct hides whatever other interfaces the real one implements,
// and Connect asserts for http.Flusher: gRPC ends every call with trailers that have to be
// flushed, and a server-streaming response that is never flushed arrives all at once when the
// handler returns. The failure is silent.
func (r *statusRecorder) Flush() {
	if f, ok := r.ResponseWriter.(http.Flusher); ok {
		f.Flush()
	}
}

// Unwrap exposes the real writer to http.NewResponseController, so capabilities this wrapper does
// not forward remain reachable.
func (r *statusRecorder) Unwrap() http.ResponseWriter { return r.ResponseWriter }
