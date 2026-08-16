// The three calls rpc/signin_browser.go makes in order, and the exact set accountapi.Service
// leaves out. Authenticating is not account management: no other module gets to reach it, which
// is why these three are absent from the interface and present only on the concrete type.

package service

import (
	"context"

	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	"__GO_MODULE__/server/internal/modules/account/domain"
	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/eventbus"
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
		// Before the password, so a deactivated account is refused without the hash comparison and
		// with its own message: "wrong password" would send someone to the reset form for nothing.
		s.failed(ctx, user.ID, device, ip)
		return domain.Account{}, errs.Unauthenticatedf(
			"This account has been deactivated. Ask an administrator to restore it.")
	}

	if !user.VerifyPassword(password) {
		// Recorded against the account that exists, so its owner can see the attempts on the
		// account page. There is nowhere to record an attempt on an address that has no account.
		s.failed(ctx, user.ID, device, ip)
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
	newDevice := s.isNewDevice(ctx, user.ID, device)
	kind := accountapi.EventSignedIn
	if newDevice {
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

	// Best-effort, and after the session exists: the session is the fact and this is the
	// convenience, so a sign-in must not be undone by an unwritable reporting column.
	_ = s.store.TouchSignIn(ctx, user.ID, now)

	eventbus.Publish(s.bus, ctx, accountapi.SignedIn{
		UserID:    user.ID,
		Email:     user.Email,
		SessionID: sess.ID,
		Device:    device,
		IPAddress: ip,
		At:        now,
		NewDevice: newDevice,
	})
	return sess, nil
}

// CompleteAuthRequest marks an in-flight authorization as authenticated: this user, via this
// session, at this moment. Browser sign-in calls it after Authenticate and StartSession succeed.
func (s *Service) CompleteAuthRequest(ctx context.Context, requestID, subject, sessionID string) error {
	return s.store.CompleteAuthRequest(ctx, requestID, subject, sessionID, s.now())
}

// failed records a rejected attempt and announces it. Both refusals go through here so the audit
// row and the published event cannot drift apart.
func (s *Service) failed(ctx context.Context, userID, device, ip string) {
	s.record(ctx, userID, accountapi.EventFailedSignIn, device, ip)
	eventbus.Publish(s.bus, ctx, accountapi.FailedSignIn{
		UserID:    userID,
		Device:    device,
		IPAddress: ip,
		At:        s.now(),
	})
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
