// Package accountapi is the account module's public contract.
//
// Other modules import this package and nothing else under internal/modules/account/. Note what
// is absent: tokens, keys, password hashes, and anything else that would let another module make
// an authorization decision of its own. Callers learn who someone is from platform/auth.Subject,
// which the middleware puts on the request context; this package is for the account management
// around that.
package accountapi

import (
	"context"
	"time"
)

// Security event kinds.
//
// These strings cross the wire to the client, which switches on them to choose a label and an
// icon. They are PascalCase because that is what it already expects; renaming one silently
// degrades the account page to showing the raw value, so treat them as part of the contract.
const (
	EventSignedIn          = "SignedIn"
	EventNewDeviceSignedIn = "NewDeviceSignedIn"
	EventFailedSignIn      = "FailedSignIn"
	EventPasswordChanged   = "PasswordChanged"
	EventSessionRevoked    = "SessionRevoked"

	// EventSignedOut is leaving on purpose; EventSessionRevoked is somebody ending the session by
	// a decision. Two kinds on purpose: docs/adr/0003-signedout-vs-sessionrevoked.md
	EventSignedOut = "SignedOut"
)

// PermissionManageUsers guards the administrative account surface.
//
// In the api package rather than beside the catalogue that declares it, because the module's own
// wire layer is mounted behind it and a package cannot import the package that imports it. An api
// package is where a module puts what something else has to name.
const PermissionManageUsers = "users.manage"

// User is an account as the rest of the application sees it.
type Account struct {
	ID          string
	Email       string
	DisplayName string
	Phone       string
	// TeamID is what a team-scoped permission resolves against. Roles are deliberately absent:
	// asking the account module which roles somebody holds is asking the wrong module, and two
	// places holding that fact is how they stop agreeing.
	TeamID string

	// TwoFactorEnabled is reported to the client's account page. Nothing enforces it yet; it is
	// here because the shape is already agreed and adding a field later is the awkward part.
	TwoFactorEnabled bool

	// LastSignInAt is zero when the account has never signed in — a state the administration
	// screen renders as "Never" and must be able to tell apart from a very old sign-in.
	LastSignInAt time.Time

	// IsActive is false for an account an administrator switched off. It cannot authenticate;
	// everything it did stays readable.
	IsActive bool

	// ActiveSessionCount is how many sessions are open. Filled in by the administrative listing
	// only; the caller's own profile leaves it zero, because that page has the session list itself
	// and a count beside it would be one more thing to keep in step.
	ActiveSessionCount int

	CreatedAt time.Time
}

// Session is one sign-in that has not been revoked or expired.
type Session struct {
	ID string

	// Client is the OAuth client the session was created for.
	Client string

	// Device is whatever the user agent claimed. Untrusted, and shown only as a hint.
	Device string

	IPAddress  string
	CreatedAt  time.Time
	LastSeenAt time.Time

	// IsCurrent marks the session that is asking. The account page uses it to hide the revoke
	// button on the row you are sitting in, which is otherwise a very easy mis-click.
	IsCurrent bool
}

// SecurityEvent is one entry in the account's audit trail.
type SecurityEvent struct {
	// Kind is one of the Event* constants above.
	Kind       string
	Device     string
	IPAddress  string
	OccurredAt time.Time
}

// Service is the account-management surface behind the /account endpoints.
type Service interface {
	// Profile returns the account, or an errs.NotFound error.
	Profile(ctx context.Context, userID string) (Account, error)

	// UpdateProfile changes the display name and phone. A nil pointer leaves that field alone,
	// which is what lets the client send a partial edit without having to read first.
	UpdateProfile(ctx context.Context, userID string, displayName, phone *string) error

	// ChangePassword re-checks the current password before replacing it, and revokes every other
	// session: a password change is usually a response to believing someone else has it.
	ChangePassword(ctx context.Context, userID, current, next string) error

	// Sessions lists the account's live sessions. currentID marks which one is asking.
	Sessions(ctx context.Context, userID, currentID string) ([]Session, error)

	// SignOut ends the caller's own session because they asked to leave.
	//
	// Mechanically identical to RevokeSession, and separate anyway: only the caller knows which of
	// the two happened, and "you signed out" and "a session was revoked" are different sentences to
	// read in a feed a week later. Collapsing them into one method is what made the activity feed
	// unable to tell them apart.
	SignOut(ctx context.Context, userID, sessionID string) error

	// RevokeSession ends one session the account owner picked out. Revoking one that is already
	// gone succeeds.
	RevokeSession(ctx context.Context, userID, sessionID string) error

	// RevokeAllSessions ends every session for the account, including the caller's.
	RevokeAllSessions(ctx context.Context, userID string) error

	// SecurityEvents returns the most recent entries, newest first.
	SecurityEvents(ctx context.Context, userID string, take int) ([]SecurityEvent, error)

	// FindByEmail resolves an address to an account, or returns an errs.NotFound error.
	//
	// The one method here that is not about the caller's own account, and it exists for one
	// reason: an administrator granting somebody access knows their email address, not the UUID
	// this server files them under. Without it the whole administrative surface takes a primary
	// key nobody can obtain, and the documented procedure becomes "run a SELECT against the
	// database".
	//
	// It is a lookup, not a search: exact address in, one account out. Nothing here enumerates
	// accounts, so it cannot be walked to discover who has one — the caller must already know the
	// address, which is the same thing the sign-in form assumes.
	FindByEmail(ctx context.Context, email string) (Account, error)
}

// SignedIn is published after a successful authentication.
type SignedIn struct {
	UserID    string
	Email     string
	SessionID string
	Device    string
	IPAddress string
	At        time.Time

	// NewDevice is true when this account has no other session from this device.
	//
	// A field rather than a second event, because it is one fact with an attribute: the sign-in
	// happened either way, and a subscriber that did not care would otherwise have to handle two
	// events to see every sign-in. The account module already decides this to choose its own audit
	// kind; this carries the answer instead of making every subscriber work it out again.
	NewDevice bool
}

// SignedOut is published when the account holder ends their own session by asking to leave.
type SignedOut struct {
	UserID    string
	SessionID string
	At        time.Time
}

// SessionRevoked is published when a session is ended by a decision rather than by leaving —
// the account owner picking a device off their list, clearing every device at once, or an
// administrator ending somebody else's.
//
// SessionID is empty when every session went at once.
type SessionRevoked struct {
	UserID    string
	SessionID string
	At        time.Time

	// ByAdmin is true when somebody other than the account holder ended it.
	//
	// The one actor distinction this server can honestly make, and it is worth making: "you ended a
	// session" and "somebody else ended your session" are the difference between a line to skim and
	// a line to act on. It is knowable because it is a different service method reached through a
	// different route behind a different permission — not because anything guesses.
	ByAdmin bool
}

// FailedSignIn is published when a password did not match, or the account was switched off.
//
// Only ever about an account that exists: there is nowhere to record an attempt on an address
// nobody has registered, so an attacker guessing addresses produces no events at all.
type FailedSignIn struct {
	UserID    string
	Device    string
	IPAddress string
	At        time.Time
}

// PasswordChanged is published after a password is successfully replaced.
type PasswordChanged struct {
	UserID string
	At     time.Time
}
