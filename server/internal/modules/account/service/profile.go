// What a signed-in user does to their own record.
//
// ChangePassword is here rather than beside the sessions it deletes: ending them is a consequence
// of replacing the credential, never something a caller asks for on its own.

package service

import (
	"context"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

// FindByEmail resolves an address to an account.
//
// Unlike everything else in this file it is not about the caller's own record: the assignments
// module uses it to turn the address an administrator typed into the id a grant is filed under.
// The normalization is the store's, so an address that differs only in case still resolves.
func (s *Service) FindByEmail(ctx context.Context, email string) (accountapi.Account, error) {
	account, err := s.store.AccountByEmail(ctx, email)
	if err != nil {
		return accountapi.Account{}, err
	}
	return toAPI(account), nil
}

// Profile returns the account.
func (s *Service) Profile(ctx context.Context, userID string) (accountapi.Account, error) {
	user, err := s.store.AccountByID(ctx, userID)
	if err != nil {
		return accountapi.Account{}, err
	}
	return toAPI(user), nil
}

// UpdateProfile changes the display name and phone.
func (s *Service) UpdateProfile(
	ctx context.Context, userID string, displayName, phone *string,
) error {
	user, err := s.store.AccountByID(ctx, userID)
	if err != nil {
		return err
	}
	if err := user.UpdateProfile(displayName, phone, s.now()); err != nil {
		return err
	}
	return s.store.UpdateAccount(ctx, user)
}

// ChangePassword replaces the password and ends every session.
func (s *Service) ChangePassword(ctx context.Context, userID, current, next string) error {
	user, err := s.store.AccountByID(ctx, userID)
	if err != nil {
		return err
	}

	now := s.now()
	if err := user.ChangePassword(current, next, now); err != nil {
		return err
	}
	if err := s.store.UpdateAccount(ctx, user); err != nil {
		return err
	}

	// A password change is usually a response to believing someone else has it. Leaving their
	// sessions alive would make the change cosmetic.
	if _, err := s.store.DeleteSessionsForUser(ctx, userID); err != nil {
		return err
	}

	s.record(ctx, userID, accountapi.EventPasswordChanged, "", "")
	eventbus.Publish(s.bus, ctx, accountapi.PasswordChanged{UserID: userID, At: now})
	return nil
}

func toAPI(u domain.Account) accountapi.Account {
	return accountapi.Account{
		LastSignInAt: u.LastSignInAt,
		IsActive:     u.IsActive,
		ID:           u.ID,
		Email:        u.Email,
		DisplayName:  u.DisplayName,
		Phone:        u.Phone,
		TeamID:       u.TeamID,
		CreatedAt:    u.CreatedAt,
	}
}
