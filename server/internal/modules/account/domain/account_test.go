package domain

import (
	"strings"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

const (
	id       = "11111111-1111-1111-1111-111111111111"
	password = "correct horse battery staple"
)

var (
	created = time.Date(2026, time.August, 1, 9, 0, 0, 0, time.UTC)
	edited  = time.Date(2026, time.August, 5, 14, 30, 0, 0, time.UTC)
)

func newValidAccount(t *testing.T) Account {
	t.Helper()

	user, err := NewAccount(id, "Ada@Example.COM ", " Ada Lovelace ", password, created)
	if err != nil {
		t.Fatalf("NewAccount returned an error: %v", err)
	}
	return user
}

func TestNewAccountNormalizesEmailAndName(t *testing.T) {
	user := newValidAccount(t)

	if user.Email != "ada@example.com" {
		t.Errorf("Email = %q, want %q", user.Email, "ada@example.com")
	}
	if user.DisplayName != "Ada Lovelace" {
		t.Errorf("DisplayName = %q, want %q", user.DisplayName, "Ada Lovelace")
	}
}

func TestNewAccountNeverStoresThePlaintext(t *testing.T) {
	user := newValidAccount(t)

	if strings.Contains(user.PasswordHash, password) {
		t.Fatal("the stored hash contains the plaintext password")
	}
	if !user.VerifyPassword(password) {
		t.Error("the password does not verify against the stored hash")
	}
	if user.VerifyPassword("something else") {
		t.Error("the wrong password verified")
	}
}

func TestNewAccountRejectsWhatIsNotAnEmailAddress(t *testing.T) {
	for _, email := range []string{"", "   ", "ada", "@example.com", "ada@", "ada example@x.com"} {
		_, err := NewAccount(id, email, "Ada", password, created)

		if err == nil {
			t.Errorf("NewAccount(%q) succeeded, want a failure", email)
			continue
		}
		if got := errs.KindOf(err); got != errs.Invalid {
			t.Errorf("NewAccount(%q) kind = %v, want %v", email, got, errs.Invalid)
		}
	}
}

func TestNewAccountRequiresADisplayName(t *testing.T) {
	if _, err := NewAccount(id, "ada@example.com", "   ", password, created); err == nil {
		t.Error("NewAccount with a blank display name succeeded, want a failure")
	}
}

func TestNewAccountEnforcesPasswordLengthAndNothingElse(t *testing.T) {
	short := strings.Repeat("a", MinPasswordLength-1)
	if _, err := NewAccount(id, "ada@example.com", "Ada", short, created); err == nil {
		t.Error("a password below the minimum was accepted")
	}

	// All lower case, no digits, no symbols — and long. Composition rules would reject this and
	// accept "Passw0rd!", which is the wrong way round.
	passphrase := strings.Repeat("a", MinPasswordLength)
	if _, err := NewAccount(id, "ada@example.com", "Ada", passphrase, created); err != nil {
		t.Errorf("a long lower-case passphrase was rejected: %v", err)
	}
}

func TestChangePassword(t *testing.T) {
	t.Run("replaces the hash and stamps the change", func(t *testing.T) {
		user := newValidAccount(t)
		before := user.PasswordHash

		if err := user.ChangePassword(password, "a whole new passphrase", edited); err != nil {
			t.Fatalf("ChangePassword returned an error: %v", err)
		}

		if user.PasswordHash == before {
			t.Error("the stored hash did not change")
		}
		if !user.VerifyPassword("a whole new passphrase") {
			t.Error("the new password does not verify")
		}
		if user.VerifyPassword(password) {
			t.Error("the old password still verifies")
		}
		if !user.UpdatedAt.Equal(edited) {
			t.Errorf("UpdatedAt = %v, want %v", user.UpdatedAt, edited)
		}
	})

	t.Run("refuses when the current password is wrong", func(t *testing.T) {
		user := newValidAccount(t)
		before := user.PasswordHash

		err := user.ChangePassword("not the password", "a whole new passphrase", edited)

		if err == nil {
			t.Fatal("ChangePassword succeeded with the wrong current password")
		}
		if user.PasswordHash != before {
			t.Error("the hash changed despite the failure")
		}
	})

	t.Run("refuses a new password identical to the old", func(t *testing.T) {
		user := newValidAccount(t)

		if err := user.ChangePassword(password, password, edited); err == nil {
			t.Error("ChangePassword accepted the same password")
		}
	})

	t.Run("refuses a new password that is too short", func(t *testing.T) {
		user := newValidAccount(t)

		if err := user.ChangePassword(password, "short", edited); err == nil {
			t.Error("ChangePassword accepted a password below the minimum")
		}
	})
}

func TestUpdateProfileLeavesNilFieldsAlone(t *testing.T) {
	user := newValidAccount(t)
	name := "Ada King"

	if err := user.UpdateProfile(&name, nil, edited); err != nil {
		t.Fatalf("UpdateProfile returned an error: %v", err)
	}

	if user.DisplayName != "Ada King" {
		t.Errorf("DisplayName = %q, want %q", user.DisplayName, "Ada King")
	}
	if user.Phone != "" {
		t.Errorf("Phone = %q, want it untouched", user.Phone)
	}
}

func TestUpdateProfileRejectsAnOverlongPhone(t *testing.T) {
	user := newValidAccount(t)
	phone := strings.Repeat("1", MaxPhoneLength+1)

	if err := user.UpdateProfile(nil, &phone, edited); err == nil {
		t.Error("UpdateProfile accepted an overlong phone number")
	}
}

func TestUpdateProfileRejectsABlankDisplayName(t *testing.T) {
	user := newValidAccount(t)
	blank := "   "

	if err := user.UpdateProfile(&blank, nil, edited); err == nil {
		t.Fatal("UpdateProfile accepted a blank display name")
	}
	if user.DisplayName != "Ada Lovelace" {
		t.Errorf("DisplayName = %q, want it unchanged", user.DisplayName)
	}
}
