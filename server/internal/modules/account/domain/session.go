package domain

import "time"

// Named UserSession rather than Session because the store's table cannot be called Session —
// SESSION is an ODBC reserved word — and a domain type that disagrees with its table by one letter
// is a type someone will mis-grep for. See the SQL rules in CLAUDE.md.
type UserSession struct {
	ID     string
	UserID string

	ClientID string

	// Whatever the user agent claimed at sign-in. Untrusted, and shown only as a hint so someone
	// can recognise their own laptop in a list.
	Device string

	IPAddress  string
	CreatedAt  time.Time
	LastSeenAt time.Time
}

// Silent token refreshes go through here, which is what keeps "last seen" honest for a client the
// user never actively opens.
func (s *UserSession) Touch(at time.Time) {
	s.LastSeenAt = at
}

// An entity inside the UserSession aggregate, not a root of its own: ending the session must end
// the token, which is why the store's foreign key cascades.
type IssuedToken struct {
	ID string

	// The owning aggregate. Never empty: a token belonging to no session is a token nothing can
	// revoke.
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

func (t IssuedToken) IsExpired(at time.Time) bool {
	return !at.Before(t.ExpiresAt)
}
