package domain

import "time"

// AuthRequest is one in-flight browser authorization, from /authorize to /token.
//
// An aggregate root with a short, self-contained life: created when the browser arrives, completed
// when the sign-in form succeeds, exchanged once for tokens, then deleted. Nothing outlives it and
// nothing else depends on it, which is why it is a root rather than part of the session — the
// session it produces survives it.
//
// It is OpenID Connect's shape rather than one we chose, and it lives here anyway: the sign-in flow
// reasons about whether a request is complete and who it belongs to, and those are rules, not
// storage details.
type AuthRequest struct {
	ID       string
	ClientID string

	// Subject is empty until the sign-in form completes the request.
	Subject string

	Scopes       []string
	RedirectURI  string
	ResponseType string
	Nonce        string
	State        string

	// CodeChallenge and its method are the PKCE binding: proof that whoever redeems the code is
	// whoever asked for it.
	CodeChallenge       string
	CodeChallengeMethod string

	// Code is the authorization code, once one has been minted for this request.
	Code string

	// SessionID is stamped on by the sign-in form: the session is created where the device and
	// address are known, and the token exchange later needs to know which session it belongs to.
	SessionID string

	Done      bool
	AuthTime  time.Time
	CreatedAt time.Time
}

// Complete marks the request authenticated for a user, via a session, at a moment.
//
// The three arrive together on purpose. A request that is done but has no subject, or a subject
// with no session, is a state the token exchange cannot act on — so it is not a state this type
// lets you reach.
func (r *AuthRequest) Complete(subject, sessionID string, at time.Time) {
	r.Subject = subject
	r.SessionID = sessionID
	r.AuthTime = at
	r.Done = true
}
