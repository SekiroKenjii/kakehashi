// What an administrator does to somebody else's account. Kept apart from profile.go because the
// caller is different: every use case there acts on the caller's own account and needs no
// permission beyond being signed in, and every one here acts on another person's and is reachable
// only behind users.manage.

package service

import (
	"context"

	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	"__GO_MODULE__/server/internal/modules/account/domain"
	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/eventbus"
)

// CreateAccount adds an account.
//
// The duplicate-address check is the store's unique index rather than a read first: two
// administrators adding the same person at the same moment is exactly the race a pre-check loses,
// and the index cannot.
func (s *Service) CreateAccount(
	ctx context.Context, email, displayName, password string,
) (accountapi.Account, error) {
	account, err := domain.NewAccount(s.newID(), email, displayName, password, s.now())
	if err != nil {
		return accountapi.Account{}, err
	}
	if err := s.store.InsertAccount(ctx, account); err != nil {
		return accountapi.Account{}, err
	}
	return toAPI(account), nil
}

// UpdateAccount edits another account's profile.
func (s *Service) UpdateAccount(
	ctx context.Context, accountID, displayName, phone, teamID string,
) (accountapi.Account, error) {
	account, err := s.store.AccountByID(ctx, accountID)
	if err != nil {
		return accountapi.Account{}, err
	}

	if err := account.UpdateProfile(&displayName, &phone, s.now()); err != nil {
		return accountapi.Account{}, err
	}
	if err := account.SetTeam(teamID, s.now()); err != nil {
		return accountapi.Account{}, err
	}
	if err := s.store.UpdateAccount(ctx, account); err != nil {
		return accountapi.Account{}, err
	}
	return toAPI(account), nil
}

// ResetPassword sets a new password and ends every session the account had.
//
// A reset happens because somebody lost control of an account or left; leaving the existing
// sessions alive would change the password and change nothing else.
func (s *Service) ResetPassword(ctx context.Context, accountID, newPassword string) error {
	account, err := s.store.AccountByID(ctx, accountID)
	if err != nil {
		return err
	}

	if err := account.ResetPassword(newPassword, s.now()); err != nil {
		return err
	}
	if err := s.store.UpdateAccount(ctx, account); err != nil {
		return err
	}
	if _, err := s.store.DeleteSessionsForUser(ctx, accountID); err != nil {
		return err
	}

	s.record(ctx, accountID, accountapi.EventPasswordChanged, "", "")
	return nil
}

// RevokeAccountSession ends one of another account's sessions.
func (s *Service) RevokeAccountSession(ctx context.Context, accountID, sessionID string) error {
	ended, err := s.store.DeleteSession(ctx, accountID, sessionID)
	if err != nil {
		return err
	}
	if !ended {
		// Nothing happened, so nothing is announced: any session id at all would otherwise put
		// "somebody else ended your session" into an account's security feed.
		return nil
	}

	s.record(ctx, accountID, accountapi.EventSessionRevoked, "", "")

	// Published with ByAdmin set so the activity feed can say "somebody else ended your session" —
	// the one line on that screen a reader acts on.
	eventbus.Publish(s.bus, ctx, accountapi.SessionRevoked{
		UserID:    accountID,
		SessionID: sessionID,
		At:        s.now(),
		ByAdmin:   true,
	})
	return nil
}

// AccountSessions lists another account's live sessions. currentID marks the caller's own session,
// so an administrator inspecting themselves still sees "this device" on the row they are in.
func (s *Service) AccountSessions(
	ctx context.Context, accountID, currentID string,
) ([]accountapi.Session, error) {
	return s.Sessions(ctx, accountID, currentID)
}

// DeleteAccount removes an account permanently.
//
// Deactivation is the recommended path and the screen says so — it keeps the history. This is for
// the record that must actually go away. The refusal to delete yourself mirrors the deactivation
// rule: an administrator who removes their own access needs a second administrator to notice,
// which is exactly when there may not be one.
func (s *Service) DeleteAccount(ctx context.Context, accountID, actor string) error {
	if accountID == actor {
		return errs.Invalidf("You cannot delete your own account.")
	}
	return s.store.DeleteAccount(ctx, accountID)
}

// Accounts lists every account, newest first.
func (s *Service) Accounts(ctx context.Context) ([]accountapi.Account, error) {
	accounts, err := s.store.Accounts(ctx)
	if err != nil {
		return nil, err
	}

	// One query for every row's session count, rather than one query per row. The screen shows the
	// number on each line, so the alternative is a list whose cost grows with the thing it lists.
	counts, err := s.store.SessionCountsByAccount(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]accountapi.Account, len(accounts))
	for i, u := range accounts {
		out[i] = toAPI(u)
		out[i].ActiveSessionCount = counts[u.ID]
	}
	return out, nil
}

// SetActive switches an account on or off, and revokes its sessions on the way down.
//
// Revoking is what makes deactivation immediate: without it the account keeps working until its
// access token expires and its refresh token is next used, minutes during which the administrator
// believes they have switched somebody off and has not.
func (s *Service) SetActive(ctx context.Context, accountID string, active bool, actor string) error {
	if accountID == actor && !active {
		// Refusing self-deactivation prevents the lockout nobody recovers from alone; a second
		// administrator can still switch this account off.
		return errs.Invalidf("You cannot deactivate your own account.")
	}

	if err := s.store.SetActive(ctx, accountID, active); err != nil {
		return err
	}
	if active {
		return nil
	}

	if _, err := s.store.DeleteSessionsForUser(ctx, accountID); err != nil {
		return err
	}
	s.record(ctx, accountID, accountapi.EventSessionRevoked, "", "")
	return nil
}
