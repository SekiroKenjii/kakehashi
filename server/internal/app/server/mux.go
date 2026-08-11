// Package server turns the mounted modules into something that answers requests.
//
// It is where the desktop original had internal/app/shell. The job is the same — collect what
// every module contributed and present it as one surface — and so is the constraint: it knows that
// modules contribute routes, and nothing about what any of them do.
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
// Two shapes share the mux, which is the reason this project uses Connect rather than grpc-go.
// A module's RPC service arrives as a path prefix and a handler; the OpenID Connect endpoints
// arrive as ordinary URLs a browser navigates to. net/http resolves between them by specificity,
// so a catch-all at "/" and a service at "/kakehashi.notes.v1.NotesService/" coexist without
// either having to know about the other.
//
// Every route carries its own policy and this enforces it. There is no list of exempt modules here
// any more, and no way to be exempt by omission: the kernel refuses at boot to collect a route with
// no policy, and refuses one that checks no permission unless the composition root named its module.
// What used to be a module-wide exemption — which skipped the check on all thirteen of the account
// module's routes, leaving its administrative service protected only by a hand-written wrapper — is
// now a decision each route states beside its pattern.
func New(k *app.Kernel) *Server {
	// ReadIdleTimeout and IdleTimeout have to be set HERE rather than on the http.Server, and that
	// is the whole reason this value is kept. An h2c connection is hijacked out of net/http's
	// tracking on its first byte, so http.Server's own timeouts never reach it — every real request
	// this server takes arrives on a connection the outer timeouts do not bound.
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

	// Resolved once, from the registry rather than by importing a module — the same move
	// authenticate makes below. With nothing registered every module is reachable, which is
	// exactly the behaviour of a server that has no access-control module mounted.
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

		// Duplicate patterns panic here, and should. Two modules claiming the same path is a
		// design mistake, and finding out at boot beats finding out from whichever one happened
		// to register second.
		mux.Handle(route.Pattern, handler)
		k.Log.Debug("route mounted",
			"pattern", route.Pattern, "module", route.Module, "policy", route.Policy.String())
	}

	// Wrapped innermost-first, so the chain reads outside-in as:
	//   recoverPanics -> otelhttp -> logRequests -> mux
	//
	// The order of the middle two is the one that matters. otelhttp puts the span on the request
	// context, so logging has to sit inside it: from there r.Context() carries the trace, and a log
	// line can be matched to the trace it came from. Outside it, the two are separate piles of
	// evidence about the same request.
	var handler http.Handler = mux
	// Authentication sits between the mux and the log so log lines can carry the caller once
	// anyone wants them to. It resolves the verifier from the registry rather than importing a
	// module — with none registered, every request is anonymous and each endpoint decides what
	// that means for it.
	//
	// Resolution sits between authentication and the mux, and applies to EVERY route rather than
	// only the gated ones. The ungated modules are the reason: authz and account are exempt from
	// the module gate by necessity, and they are also the two that serve administrative endpoints
	// needing roles.manage and users.manage. Resolving only inside the gate would leave exactly
	// those handlers with nothing to check against.
	//
	// One query per authenticated request, which is a primary-key lookup on a small table. Making
	// it lazy would save it on the routes that never ask, at the cost of a memoising resolver on
	// the context and an error nobody has a good place to handle.
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

	// h2c serves HTTP/2 without TLS.
	//
	// gRPC requires HTTP/2, and Go's net/http only negotiates it over TLS. In production this
	// server sits behind a proxy that terminates TLS and speaks cleartext HTTP/2 to it; in
	// development there is no proxy and no certificate. Without h2c, both cases fail with an
	// error about the protocol rather than about the missing TLS, which is a confusing way to
	// spend an afternoon.
	//
	// It is not a security decision: the transport is trusted or it is not, and that is settled
	// by what runs in front of this process, not here.
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
// The waiting is the part that is easy to leave out and expensive to leave out. h2c hijacks every
// HTTP/2 connection out of net/http's tracking, so http.Server.Shutdown has nothing to drain and
// returns in milliseconds — which used to mean the caller went straight on to closing the SQL and
// Mongo pools underneath handlers that were still running. A sign-in would read the account, lose
// its database mid-flight, and answer a reset stream. ConfigureServer installs the HTTP/2 graceful
// shutdown hook so peers get a GOAWAY; the WaitGroup is what actually makes the caller wait.
func (s *Server) Run(ctx context.Context) error {
	httpServer := &http.Server{
		Addr:    s.kernel.Cfg.Addr,
		Handler: s.handler,

		// ReadHeaderTimeout and nothing else. A blanket ReadTimeout would also cap how long a
		// request body may take, which silently breaks client-streaming RPCs the moment someone
		// adds one; this bounds only the part that a Slowloris attack abuses.
		ReadHeaderTimeout: 10 * time.Second,
		IdleTimeout:       120 * time.Second,
	}

	// Registers the HTTP/2 graceful-shutdown hook on httpServer. Without it the h2s this server
	// built is a bag of settings nothing consults at shutdown.
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
// requests are using, and "we closed the database on three live requests" is a log line somebody
// needs when a shutdown produces a handful of unexplained client errors.
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
			// Fail closed. The alternative is that an unreachable policy store silently opens
			// every gated module, which is the failure you least want to be quiet. Signing in is
			// unaffected: it is an anonymous request and returned above.
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
// This is the enforcement point of the whole access model, and it is one wrapper per route rather
// than a check per handler on purpose: a handler that forgets the check is a handler nothing
// catches, and the one somebody forgets is the breach. Here a route is checked because it declared
// a policy, not because its author remembered to wrap it — the version this replaces required
// exactly that wrap, in three module.go files, with nothing to catch its deletion.
//
// It sits inside authenticate, so the Subject is already on the context, and inside logRequests,
// so every refusal is one ordinary request line with its 403 — a refused caller shows up in the
// same place as everything else rather than in a channel somebody has to know to look at.
func requirePermission(permission string, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if _, ok := auth.SubjectFrom(r.Context()); !ok {
			// Anonymous, and refused. An earlier version passed these through on the reasoning
			// that a gated module might still have an endpoint welcoming anonymous callers — which
			// made the whole gate optional, because a caller who simply omitted the Authorization
			// header met no check at all. Sending no token is not a way to have more permissions
			// than sending one.
			deny(w, http.StatusUnauthorized, "unauthenticated",
				"This endpoint requires a signed-in caller.")
			return
		}

		if !auth.GrantsFrom(r.Context()).Allows(permission) {
			// 403 rather than 404, and it names the permission. The caller knows this endpoint
			// exists — it is compiled into the client they are running — so hiding it buys nothing
			// and costs the one thing that makes the refusal actionable: "ask an administrator for
			// X" is only sayable if the client is told what X is.
			deny(w, http.StatusForbidden, "forbidden",
				"This endpoint requires the "+permission+" permission.")
			return
		}

		next.ServeHTTP(w, r)
	})
}

// deny writes the refusal in the error envelope this server's REST surface is pinned to.
//
// {"error","message"} is what docs/CONTRACTS.md documents and what the client's gateway parses, so
// a refusal reaches a person as a sentence rather than as a status code. A Connect client cannot
// read a body this middleware writes — it runs before Connect sees the request — but the REST
// endpoints under /account can, and writing two shapes would be one shape too many.
func deny(w http.ResponseWriter, status int, code, message string) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]string{"error": code, "message": message})
}

// recoverPanics keeps one bad request from taking down the process.
//
// net/http already recovers per connection, but it kills the connection and logs to a place
// nobody watches. This turns the panic into a 500 the caller can act on and a log line with the
// request attached, which is the difference between "something crashed" and knowing what.
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
// This is not optional decoration. Embedding a ResponseWriter in a struct hides whatever other
// interfaces the real one implements, and Connect asserts for http.Flusher: gRPC ends every call
// with trailers that have to be flushed, and a server-streaming response that is never flushed
// arrives all at once when the handler returns, which is the opposite of streaming. The failure is
// silent, so it is worth the eight lines.
func (r *statusRecorder) Flush() {
	if f, ok := r.ResponseWriter.(http.Flusher); ok {
		f.Flush()
	}
}

// Unwrap exposes the real writer to http.NewResponseController, which is how anything else that
// needs a capability this wrapper did not think of can still reach it.
func (r *statusRecorder) Unwrap() http.ResponseWriter { return r.ResponseWriter }
