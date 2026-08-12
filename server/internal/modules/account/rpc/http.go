package rpc

import (
	"encoding/json"
	"net"
	"net/http"
	"strings"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// The error shape is pinned by the client: {"error","message"} is what AccountGateway parses, so
// it is a contract rather than a convention. See docs/CONTRACTS.md.

// Every handler starts here: these endpoints have no anonymous mode.
func requireSubject(w http.ResponseWriter, r *http.Request) (auth.Subject, bool) {
	subject, ok := auth.SubjectFrom(r.Context())
	if !ok {
		writeStatus(w, http.StatusUnauthorized, errorResponse{
			Error:   "unauthenticated",
			Message: "This endpoint requires a signed-in caller.",
		})
		return auth.Subject{}, false
	}
	return subject, true
}

func readJSON(w http.ResponseWriter, r *http.Request, into any) bool {
	decoder := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1<<20))
	if err := decoder.Decode(into); err != nil {
		writeStatus(w, http.StatusBadRequest, errorResponse{
			Error:   "malformed_request",
			Message: "The request body is not the JSON this endpoint expects.",
		})
		return false
	}
	return true
}

func writeJSON(w http.ResponseWriter, payload any) {
	writeStatus(w, http.StatusOK, payload)
}

func writeStatus(w http.ResponseWriter, status int, payload any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(payload)
}

// Maps a service error onto the shape AccountGateway.ReadErrorAsync parses. The same
// kind-to-status thinking as the Connect interceptor, restated for plain HTTP because these
// endpoints do not go through Connect.
func writeError(w http.ResponseWriter, err error) {
	kind := errs.KindOf(err)

	status := http.StatusInternalServerError
	switch kind {
	case errs.NotFound:
		status = http.StatusNotFound
	case errs.Invalid:
		status = http.StatusBadRequest
	case errs.Conflict:
		status = http.StatusConflict
	case errs.Unauthenticated:
		status = http.StatusUnauthorized
	case errs.Forbidden:
		status = http.StatusForbidden
	}

	writeStatus(w, status, errorResponse{
		Error: kind.String(),
		// PublicMessage collapses Internal errors to a fixed string, so nothing about the
		// database's inner life reaches a caller.
		Message: errs.PublicMessage(err),
	})
}

// What the audit trail records about a request. Both are claims, not facts — the user agent lies
// freely and the address may be a proxy — which is why they are only ever displayed, never used
// for decisions.
func callerFacts(r *http.Request) (device, ip string) {
	device = strings.TrimSpace(r.UserAgent())
	if len(device) > 256 {
		device = device[:256]
	}

	// Behind the reverse proxy the peer address is the proxy's; the original is in the header it
	// appends. First value wins: everything after it was added by hops we trust less.
	if forwarded := r.Header.Get("X-Forwarded-For"); forwarded != "" {
		ip = strings.TrimSpace(strings.Split(forwarded, ",")[0])
	} else if host, _, err := net.SplitHostPort(r.RemoteAddr); err == nil {
		ip = host
	} else {
		ip = r.RemoteAddr
	}
	return device, ip
}
