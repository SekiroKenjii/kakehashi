package domain

import "time"

// UserSession is one sign-in, and the aggregate root over the tokens issued under it.
//
// The name is UserSession rather than Session because the store's table cannot be called Session —
// SESSION is an ODBC reserved word — and a domain type that disagrees with its table by one letter
// is a type someone will mis-grep for. See the SQL rules in CLAUDE.md.
type UserSession struct {
	ID     string
	UserID string

	// ClientID is the OAuth client the session belongs to.
	ClientID string

	// Device is whatever the user agent claimed at sign-in. Untrusted, and shown only as a hint
	// so someone can recognise their own laptop in a list.
	Device string

	IPAddress  string
	CreatedAt  time.Time
	LastSeenAt time.Time
}

// Touch records that the session was used. Silent token refreshes go through here, which is what
// keeps "last seen" honest for a client the user never actively opens.
func (s *UserSession) Touch(at time.Time) {
	s.LastSeenAt = at
}

// IssuedToken is an access token, and the refresh token it was issued with when there is one.
//
// An entity inside the UserSession aggregate, not a root of its own: a token has no life without
// the session that issued it, and ending the session must end the token. That is a consistency
// rule rather than a cleanup preference, which is exactly what makes the session the boundary and
// why the store's foreign key cascades.
type IssuedToken struct {
	ID string

	// SessionID is the owning aggregate. Never empty: a token belonging to no session is a token
	// nothing can revoke.
	SessionID string

	AccountID    string
	ClientID     string
	RefreshToken string
	Scopes       []string
	Audience     []string
	AuthTime     time.Time
	ExpiresAt    time.Time
	CreatedAt    time.Time
}

// IsExpired reports whether the token is past its lifetime at the given moment.
func (t IssuedToken) IsExpired(at time.Time) bool {
	return !at.Before(t.ExpiresAt)
}
