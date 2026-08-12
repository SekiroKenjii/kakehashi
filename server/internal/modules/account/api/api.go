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

// These strings cross the wire to the client, which switches on them to choose a label and an
// icon. They are PascalCase because that is what it already expects; renaming one silently
// degrades the account page to showing the raw value, so treat them as part of the contract.
const (
	EventSignedIn          = "SignedIn"
	EventNewDeviceSignedIn = "NewDeviceSignedIn"
	EventFailedSignIn      = "FailedSignIn"
	EventPasswordChanged   = "PasswordChanged"
	EventSessionRevoked    = "SessionRevoked"

	// Leaving on purpose. This used to be recorded as EventSessionRevoked, so the account page told
	// people a session had been revoked every time they signed out.
	EventSignedOut = "SignedOut"
)

// In the api package rather than beside the catalogue that declares it, because the module's own
// wire layer is mounted behind it and a package cannot import the package that imports it.
const PermissionManageUsers = "users.manage"

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

	// False for an account an administrator switched off: it cannot authenticate, and everything it
	// did stays readable.
	IsActive bool

	// Filled in by the administrative listing only; the caller's own profile leaves it zero,
	// because that page has the session list itself and a count beside it would be one more thing
	// to keep in step.
	ActiveSessionCount int

	CreatedAt time.Time
}

// One sign-in that has not been revoked or expired.
type Session struct {
	ID string

	Client string

	// Whatever the user agent claimed. Untrusted, and shown only as a hint.
	Device string

	IPAddress  string
	CreatedAt  time.Time
	LastSeenAt time.Time

	// Marks the session that is asking. The account page uses it to hide the revoke button on the
	// row you are sitting in, which is otherwise a very easy mis-click.
	IsCurrent bool
}

type SecurityEvent struct {
	// One of the Event* constants above.
	Kind       string
	Device     string
	IPAddress  string
	OccurredAt time.Time
}

type Service interface {
	// Returns an errs.NotFound error when there is no such account.
	Profile(ctx context.Context, userID string) (Account, error)

	// A nil pointer leaves that field alone, which is what lets the client send a partial edit
	// without having to read first.
	UpdateProfile(ctx context.Context, userID string, displayName, phone *string) error

	// Re-checks the current password before replacing it, and revokes every other session: a
	// password change is usually a response to believing someone else has it.
	ChangePassword(ctx context.Context, userID, current, next string) error

	Sessions(ctx context.Context, userID, currentID string) ([]Session, error)

	// Mechanically identical to RevokeSession, and separate anyway: only the caller knows which of
	// the two happened, and "you signed out" and "a session was revoked" are different sentences to
	// read in a feed a week later. Collapsing them into one method is what made the activity feed
	// unable to tell them apart.
	SignOut(ctx context.Context, userID, sessionID string) error

	// Revoking a session that is already gone succeeds.
	RevokeSession(ctx context.Context, userID, sessionID string) error

	// Including the caller's own.
	RevokeAllSessions(ctx context.Context, userID string) error

	// Newest first.
	SecurityEvents(ctx context.Context, userID string, take int) ([]SecurityEvent, error)

	// Returns an errs.NotFound error when there is no such account.
	//
	// The one method here that is not about the caller's own account, and it exists for one
	// reason: an administrator granting somebody access knows their email address, not the UUID
	// this server files them under. Without it the whole administrative surface takes a primary
	// key nobody can obtain.
	//
	// It is a lookup, not a search: exact address in, one account out. Nothing here enumerates
	// accounts, so it cannot be walked to discover who has one — the caller must already know the
	// address, which is the same thing the sign-in form assumes.
	FindByEmail(ctx context.Context, email string) (Account, error)
}

type SignedIn struct {
	UserID    string
	Email     string
	SessionID string
	Device    string
	IPAddress string
	At        time.Time

	// True when this account has no other session from this device.
	//
	// A field rather than a second event: the sign-in happened either way, and a subscriber that
	// did not care would otherwise have to handle two events to see every sign-in.
	NewDevice bool
}

// Published when the account holder ends their own session by asking to leave, never when one is
// revoked.
type SignedOut struct {
	UserID    string
	SessionID string
	At        time.Time
}

// Published when a session is ended by a decision rather than by leaving — the account owner
// picking a device off their list, clearing every device at once, or an administrator ending
// somebody else's.
//
// SessionID is empty when every session went at once.
type SessionRevoked struct {
	UserID    string
	SessionID string
	At        time.Time

	// True when somebody other than the account holder ended it. Knowable because that is a
	// different service method reached through a different route behind a different permission —
	// nothing guesses.
	ByAdmin bool
}

// Published when a password did not match, or the account was switched off.
//
// Only ever about an account that exists: there is nowhere to record an attempt on an address
// nobody has registered, so an attacker guessing addresses produces no events at all.
type FailedSignIn struct {
	UserID    string
	Device    string
	IPAddress string
	At        time.Time
}

type PasswordChanged struct {
	UserID string
	At     time.Time
}
