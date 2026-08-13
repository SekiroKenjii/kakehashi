package domain

import (
	"strings"
	"time"
	"unicode/utf8"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/passwords"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/text"
)

// Limits on the fields a user controls. All of them are about the interface rather than the
// storage, and all of them are matched by the column widths in store/.
const (
	MaxEmailLength       = 254 // the longest address SMTP will carry
	MaxDisplayNameLength = 100
	MaxPhoneLength       = 32
	MaxTeamLength        = 64 // matches the TeamId column
	MinPasswordLength    = 12
)

// Account is the entity.
//
// PasswordHash is exported for the store to scan into, but it is never the plaintext and never
// leaves the module: accountapi.Account has no such field, and the mapping in service/ is what
// guarantees it.
type Account struct {
	ID           string
	Email        string
	DisplayName  string
	Phone        string
	PasswordHash string
	// TeamID is what the "team" row scope resolves against. Empty means the account belongs to no
	// team, and a team-scoped grant then reaches nothing — the safe reading.
	TeamID string

	// LastSignInAt is zero until the account has signed in once. A zero time rather than a pointer
	// because no rule here distinguishes "never" from "long ago"; the store maps it to and from
	// NULL at the edge, where the distinction is the screen's to make.
	LastSignInAt time.Time

	// IsActive is the switch an administrator throws instead of deleting. A deactivated account
	// cannot authenticate; everything it did remains readable.
	IsActive bool

	CreatedAt time.Time
	UpdatedAt time.Time
}

// ResetPassword replaces the password without checking the current one.
//
// Distinct from the owner's own change, which verifies the current password first. An
// administrator does not have it — that is why this exists — so the protection here is not a
// second factor but who is allowed to call it: the caller needs users.manage, and every reset
// revokes the account's sessions.
func (a *Account) ResetPassword(plainPassword string, now time.Time) error {
	if err := checkPasswordStrength(plainPassword); err != nil {
		return err
	}

	hash, err := passwords.Hash(plainPassword)
	if err != nil {
		return errs.Internalf(err, "hash password")
	}

	a.PasswordHash = hash
	a.UpdatedAt = now
	return nil
}

// SetTeam moves the account between teams. Empty means no team, which narrows every team-scoped
// grant to nothing — a real choice, so it is allowed rather than silently ignored.
func (a *Account) SetTeam(teamID string, now time.Time) error {
	trimmed := strings.TrimSpace(teamID)
	if text.UTF16Len(trimmed) > MaxTeamLength {
		return errs.Invalidf("A team is limited to %d characters.", MaxTeamLength)
	}

	a.TeamID = trimmed
	a.UpdatedAt = now
	return nil
}

// NewAccount builds a valid user with the password already hashed, or explains why it cannot.
//
// id and now are passed in rather than generated here so tests can pin both. A domain that reaches
// for uuid.New and time.Now is a domain whose tests have to assert on shapes instead of values.
func NewAccount(id, email, displayName, plainPassword string, now time.Time) (Account, error) {
	normalizedEmail, err := normalizeEmail(email)
	if err != nil {
		return Account{}, err
	}
	name, err := normalizeDisplayName(displayName)
	if err != nil {
		return Account{}, err
	}
	if err := checkPasswordStrength(plainPassword); err != nil {
		return Account{}, err
	}

	hash, err := passwords.Hash(plainPassword)
	if err != nil {
		return Account{}, errs.Internalf(err, "hash password")
	}

	return Account{
		ID:           id,
		Email:        normalizedEmail,
		DisplayName:  name,
		PasswordHash: hash,
		// Active from the moment it exists. An account that had to be switched on afterwards is a
		// second step every caller must remember, and whoever forgets has created an account that
		// cannot sign in for reasons nothing explains.
		IsActive:  true,
		CreatedAt: now,
		UpdatedAt: now,
	}, nil
}

// VerifyPassword reports whether plain is this user's password.
//
// A malformed stored hash reports false, not an error: from the outside, an account whose hash is
// corrupt and an account whose password is wrong must be indistinguishable, or the difference
// becomes a way to enumerate accounts.
func (u Account) VerifyPassword(plain string) bool {
	ok, err := passwords.Verify(u.PasswordHash, plain)
	return err == nil && ok
}

// NeedsPasswordRehash reports whether the stored hash was made with weaker parameters than the
// current ones. Check it after a successful sign-in, which is the only moment the plaintext is in
// hand and the hash can be upgraded without asking the user for anything.
func (u Account) NeedsPasswordRehash() bool {
	return passwords.NeedsRehash(u.PasswordHash)
}

// ChangePassword replaces the password after re-checking the current one.
func (u *Account) ChangePassword(current, next string, now time.Time) error {
	if !u.VerifyPassword(current) {
		// Deliberately not "wrong password": this path is reached by an already-authenticated
		// caller, so the message can be direct without helping anyone enumerate anything.
		return errs.Invalidf("Your current password is not correct.")
	}
	if current == next {
		return errs.Invalidf("The new password must be different from the current one.")
	}
	if err := checkPasswordStrength(next); err != nil {
		return err
	}

	hash, err := passwords.Hash(next)
	if err != nil {
		return errs.Internalf(err, "hash password")
	}

	u.PasswordHash = hash
	u.UpdatedAt = now
	return nil
}

// RehashPassword replaces the stored hash with one made at the current cost, without changing the
// password. Only meaningful immediately after VerifyPassword returned true.
func (u *Account) RehashPassword(plain string, now time.Time) error {
	hash, err := passwords.Hash(plain)
	if err != nil {
		return errs.Internalf(err, "hash password")
	}
	u.PasswordHash = hash
	u.UpdatedAt = now
	return nil
}

// UpdateProfile changes the display name and phone. A nil pointer leaves that field alone, so a
// caller can send a partial edit without having to read the record first.
func (u *Account) UpdateProfile(displayName, phone *string, now time.Time) error {
	if displayName != nil {
		name, err := normalizeDisplayName(*displayName)
		if err != nil {
			return err
		}
		u.DisplayName = name
	}

	if phone != nil {
		trimmed := strings.TrimSpace(*phone)
		if text.UTF16Len(trimmed) > MaxPhoneLength {
			return errs.Invalidf("Phone numbers are limited to %d characters.", MaxPhoneLength)
		}
		u.Phone = trimmed
	}

	u.UpdatedAt = now
	return nil
}

func normalizeEmail(email string) (string, error) {
	// Lower-cased and trimmed, because the address is the account's identity and users do not
	// remember which case they typed. The local part is technically case-sensitive per RFC 5321;
	// no mail provider anyone uses actually treats it that way, and honouring it would create two
	// accounts for one person.
	trimmed := strings.ToLower(strings.TrimSpace(email))

	if trimmed == "" {
		return "", errs.Invalidf("An email address is required.")
	}
	if text.UTF16Len(trimmed) > MaxEmailLength {
		return "", errs.Invalidf("That email address is too long.")
	}

	// Deliberately not a regular expression. Every address-validating regex is either wrong or
	// unreadable, and the only real proof an address exists is that mail sent to it arrives. This
	// rejects what is obviously not an address and leaves the rest to delivery.
	at := strings.IndexByte(trimmed, '@')
	if at <= 0 || at == len(trimmed)-1 || strings.ContainsAny(trimmed, " \t\r\n") {
		return "", errs.Invalidf("That does not look like an email address.")
	}

	return trimmed, nil
}

func normalizeDisplayName(name string) (string, error) {
	trimmed := strings.TrimSpace(name)

	if trimmed == "" {
		return "", errs.Invalidf("A display name is required.")
	}
	if text.UTF16Len(trimmed) > MaxDisplayNameLength {
		return "", errs.Invalidf(
			"Display names are limited to %d characters.", MaxDisplayNameLength)
	}
	return trimmed, nil
}

// checkPasswordStrength enforces length and nothing else.
//
// No "must contain a digit and a symbol": composition rules push people towards Passw0rd! and away
// from the long passphrases that are actually strong. NIST dropped them for this reason, and
// length is the rule that survives.
func checkPasswordStrength(plain string) error {
	if utf8.RuneCountInString(plain) < MinPasswordLength {
		return errs.Invalidf(
			"Passwords must be at least %d characters.", MinPasswordLength)
	}
	return nil
}
