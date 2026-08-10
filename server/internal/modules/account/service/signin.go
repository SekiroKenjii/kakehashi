// The three calls rpc/signin_browser.go makes in order, and the exact set accountapi.Service
// leaves out. Authenticating is not account management: no other module gets to reach it, which
// is why these three are absent from the interface and present only on the concrete type.

package service

import (
	"context"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

// Authenticate checks an email and password, recording the attempt either way.
//
// The error is the same whether the address is unknown or the password is wrong. Distinguishing
// them turns the sign-in form into an account-enumeration oracle, which is how a leaked password
// list gets matched against your user base.
func (s *Service) Authenticate(
	ctx context.Context, email, password, device, ip string,
) (domain.Account, error) {
	const rejected = "That email address and password do not match an account."

	user, err := s.store.AccountByEmail(ctx, email)
	if errs.KindOf(err) == errs.NotFound {
		return domain.Account{}, errs.Unauthenticatedf(rejected)
	}
	if err != nil {
		return domain.Account{}, err
	}

	if !user.IsActive {
		// Checked before the password, so a deactivated account is refused without the hash
		// comparison — and refused with its own message, because "your account has been
		// deactivated" is actionable and "wrong password" sends someone to the reset form for
		// nothing. The address is known to exist by the administrator who switched it off, so this
		// message enumerates nothing that was private.
		s.record(ctx, user.ID, accountapi.EventFailedSignIn, device, ip)
		return domain.Account{}, errs.Unauthenticatedf(
			"This account has been deactivated. Ask an administrator to restore it.")
	}

	if !user.VerifyPassword(password) {
		// Recorded against the account that exists, so its owner can see the attempts on the
		// account page. There is nowhere to record an attempt on an address that has no account.
		s.record(ctx, user.ID, accountapi.EventFailedSignIn, device, ip)
		return domain.Account{}, errs.Unauthenticatedf(rejected)
	}

	// The one moment the plaintext is in hand, so the one moment the stored hash can be upgraded
	// to the current cost without asking the user for anything.
	if user.NeedsPasswordRehash() {
		if err := user.RehashPassword(password, s.now()); err == nil {
			_ = s.store.UpdateAccount(ctx, user)
		}
	}

	return user, nil
}

// StartSession records a sign-in and announces it.
func (s *Service) StartSession(
	ctx context.Context, user domain.Account, clientID, device, ip string,
) (domain.UserSession, error) {
	// Asked before the insert, or the session being created would put its own device on the list
	// and nothing would ever count as new.
	kind := accountapi.EventSignedIn
	if s.isNewDevice(ctx, user.ID, device) {
		// Worth its own kind: "someone signed in from a machine you have not used before" is the
		// line in an audit trail people actually react to.
		kind = accountapi.EventNewDeviceSignedIn
	}

	now := s.now()
	sess := domain.UserSession{
		ID:         s.newID(),
		UserID:     user.ID,
		ClientID:   clientID,
		Device:     device,
		IPAddress:  ip,
		CreatedAt:  now,
		LastSeenAt: now,
	}

	if err := s.store.InsertSession(ctx, sess); err != nil {
		return domain.UserSession{}, err
	}

	s.record(ctx, user.ID, kind, device, ip)

	// Best-effort, and after the session exists. A sign-in that succeeded must not be undone
	// because a reporting column could not be written; the session is the fact, this is the
	// convenience.
	_ = s.store.TouchSignIn(ctx, user.ID, now)

	eventbus.Publish(s.bus, ctx, accountapi.SignedIn{
		UserID:    user.ID,
		Email:     user.Email,
		SessionID: sess.ID,
		Device:    device,
		IPAddress: ip,
		At:        now,
	})
	return sess, nil
}

// CompleteAuthRequest marks an in-flight authorization as authenticated: this user, via this
// session, at this moment. Browser sign-in calls it after Authenticate and StartSession succeed.
func (s *Service) CompleteAuthRequest(ctx context.Context, requestID, subject, sessionID string) error {
	return s.store.CompleteAuthRequest(ctx, requestID, subject, sessionID, s.now())
}

func (s *Service) isNewDevice(ctx context.Context, userID, device string) bool {
	if device == "" {
		return false
	}
	sessions, err := s.store.SessionsForUser(ctx, userID)
	if err != nil {
		return false
	}
	for _, sess := range sessions {
		if sess.Device == device {
			return false
		}
	}
	return true
}
