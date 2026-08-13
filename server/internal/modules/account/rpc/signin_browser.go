package rpc

import (
	"context"
	"html/template"
	"net/http"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/service"
)

// browserSignInHandler serves the form the /authorize endpoint redirects to, and completes the
// authorization when the credentials check out.
type browserSignInHandler struct {
	svc      *service.Service
	clientID string

	// callbackURL builds the address to send the browser back to once the request is done —
	// op's authorize callback, which mints the code and redirects to the client's loopback.
	callbackURL func(context.Context, string) string
}

// browserSignInPage is deliberately server-rendered, dependency-free HTML.
//
// This form is the security boundary of the whole system: it is where passwords cross the wire.
// A framework bundle here would mean auditing a build pipeline to trust a login page. Sixty lines
// of template need no audit.
var browserSignInPage = template.Must(template.New("login").Parse(`<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Sign in — Kakehashi</title>
<style>
  :root { color-scheme: light dark; }
  body {
    font-family: "Segoe UI", system-ui, sans-serif;
    display: grid; place-items: center; min-height: 100vh; margin: 0;
    background: light-dark(#f3f3f3, #202020);
    color: light-dark(#1a1a1a, #f0f0f0);
  }
  main {
    width: min(360px, calc(100vw - 48px));
    background: light-dark(#ffffff, #2b2b2b);
    border: 1px solid light-dark(#e0e0e0, #1a1a1a);
    border-radius: 8px; padding: 32px;
  }
  h1 { font-size: 20px; margin: 0 0 4px; }
  p.sub { margin: 0 0 24px; font-size: 13px; color: light-dark(#616161, #9e9e9e); }
  label { display: block; font-size: 13px; margin: 14px 0 4px; }
  input {
    width: 100%; box-sizing: border-box; padding: 8px 10px; font-size: 14px;
    border: 1px solid light-dark(#bdbdbd, #4a4a4a); border-radius: 4px;
    background: light-dark(#ffffff, #1f1f1f); color: inherit;
  }
  button {
    width: 100%; margin-top: 22px; padding: 9px; font-size: 14px; font-weight: 600;
    border: none; border-radius: 4px; background: #4f6bed; color: white; cursor: pointer;
  }
  .error {
    margin: 0 0 16px; padding: 10px 12px; font-size: 13px; border-radius: 4px;
    background: light-dark(#fde7e9, #442726); color: light-dark(#a4262c, #f1707b);
  }
</style>
</head>
<body>
<main>
  <h1>Sign in to Kakehashi</h1>
  <p class="sub">The desktop app is waiting for you to finish here.</p>
  {{if .Error}}<p class="error">{{.Error}}</p>{{end}}
  <form method="post" action="/account/browser/sign-in">
    <input type="hidden" name="authRequestID" value="{{.AuthRequestID}}">
    <label for="email">Email</label>
    <input id="email" name="email" type="email" value="{{.Email}}" required autofocus autocomplete="username">
    <label for="password">Password</label>
    <input id="password" name="password" type="password" required autocomplete="current-password">
    <button type="submit">Sign in</button>
  </form>
</main>
</body>
</html>`))

type browserSignInPageData struct {
	AuthRequestID string
	Email         string
	Error         string
}

func (h *browserSignInHandler) showForm(w http.ResponseWriter, r *http.Request) {
	h.render(w, browserSignInPageData{AuthRequestID: r.URL.Query().Get("authRequestID")})
}

func (h *browserSignInHandler) submit(w http.ResponseWriter, r *http.Request) {
	if err := r.ParseForm(); err != nil {
		http.Error(w, "malformed form", http.StatusBadRequest)
		return
	}

	requestID := r.PostFormValue("authRequestID")
	email := r.PostFormValue("email")
	password := r.PostFormValue("password")
	if requestID == "" {
		// No request id means the browser did not arrive here via /authorize, so there is no
		// authorization to complete.
		http.Error(w, "this page is only reachable from a sign-in request", http.StatusBadRequest)
		return
	}

	device, ip := callerFacts(r)

	account, err := h.svc.Authenticate(r.Context(), email, password, device, ip)
	if err != nil {
		// The service's message is deliberately the same for a wrong password and an unknown
		// address; render it and keep what the user typed, except the password.
		h.render(w, browserSignInPageData{
			AuthRequestID: requestID,
			Email:         email,
			Error:         "That email address and password do not match an account.",
		})
		return
	}

	session, err := h.svc.StartSession(r.Context(), account, h.clientID, device, ip)
	if err != nil {
		http.Error(w, "could not start a session", http.StatusInternalServerError)
		return
	}

	if err := h.svc.CompleteAuthRequest(
		r.Context(), requestID, account.ID, session.ID); err != nil {
		http.Error(w, "that sign-in request has expired", http.StatusBadRequest)
		return
	}

	http.Redirect(w, r, h.callbackURL(r.Context(), requestID), http.StatusFound)
}

func (h *browserSignInHandler) render(w http.ResponseWriter, data browserSignInPageData) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	// This page handles credentials: forbid framing and caching.
	w.Header().Set("X-Frame-Options", "DENY")
	w.Header().Set("Cache-Control", "no-store")
	_ = browserSignInPage.Execute(w, data)
}
