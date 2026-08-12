package service

import (
	"context"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// The slice of the account module this one needs, declared as a consumer-owned port so these use
// cases stay testable against a fake. accountapi.Service satisfies it.
type Accounts interface {
	FindByEmail(ctx context.Context, email string) (accountapi.Account, error)
	Profile(ctx context.Context, accountID string) (accountapi.Account, error)
}

// Injected after construction because the account module publishes its service during Register,
// and a module may not resolve another's during its own — this has to happen in Start.
func (s *Service) WithAccounts(accounts Accounts) {
	s.accounts = accounts
}

// Resolving the address to an id here rather than in the client keeps UUIDs off the wire and puts
// the lookup on the side that can check the address exists.
//
// The refusal names the address: an administrator who mistyped one character needs to see which
// address the server looked for, not that "the account was not found".
func (s *Service) resolve(ctx context.Context, email string) (accountapi.Account, error) {
	if s.accounts == nil {
		return accountapi.Account{}, errs.Internalf(nil, "No account lookup is wired.")
	}
	account, err := s.accounts.FindByEmail(ctx, email)
	if errs.KindOf(err) == errs.NotFound {
		return accountapi.Account{}, errs.NotFoundf("No account exists for %s.", email)
	}
	return account, err
}

// Without this a fresh deployment is locked out of itself: every module is gated, nobody holds a
// role, and the screen that would grant one is reachable only by somebody who already holds it.
//
// Idempotent, and silent when the address has no account — a deployment that has not created its
// administrator yet should still boot, and the missing account is the operator's to notice.
func (s *Service) BootstrapAdmin(ctx context.Context, email string) error {
	if email == "" || s.accounts == nil {
		return nil
	}

	account, err := s.accounts.FindByEmail(ctx, email)
	if errs.KindOf(err) == errs.NotFound {
		return nil
	}
	if err != nil {
		return err
	}

	admin, err := s.store.RoleByName(ctx, AdminRoleName)
	if err != nil {
		return err
	}
	return s.store.AssignRole(ctx, account.ID, admin.ID, "system", s.now())
}
