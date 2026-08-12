package domain

import "time"

// One in-flight browser authorization, from /authorize to /token: created when the browser
// arrives, completed when the sign-in form succeeds, exchanged once for tokens, then deleted. A
// root rather than part of the session, because the session it produces survives it.
//
// It is OpenID Connect's shape rather than one we chose, and it lives here anyway: the sign-in flow
// reasons about whether a request is complete and who it belongs to, and those are rules, not
// storage details.
type AuthRequest struct {
	ID       string
	ClientID string

	// Empty until the sign-in form completes the request.
	Subject string

	Scopes       []string
	RedirectURI  string
	ResponseType string
	Nonce        string
	State        string

	// The PKCE binding: proof that whoever redeems the code is whoever asked for it.
	CodeChallenge       string
	CodeChallengeMethod string

	// Empty until an authorization code has been minted for this request.
	Code string

	// Stamped on by the sign-in form: the session is created where the device and address are
	// known, and the token exchange later needs to know which session it belongs to.
	SessionID string

	Done      bool
	AuthTime  time.Time
	CreatedAt time.Time
}

// The three arrive together on purpose. A request that is done but has no subject, or a subject
// with no session, is a state the token exchange cannot act on — so it is not a state this type
// lets you reach.
func (r *AuthRequest) Complete(subject, sessionID string, at time.Time) {
	r.Subject = subject
	r.SessionID = sessionID
	r.AuthTime = at
	r.Done = true
}
