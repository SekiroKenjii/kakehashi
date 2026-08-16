package service

import (
	"context"

	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	"__GO_MODULE__/server/internal/platform/errs"
)

// The module's one dependency on another module, and the only file that names it.
//
// An administrator names a person by their email address; a grant is filed under the account's id.
// Resolving here rather than in the client means the wire never carries a UUID somebody would have
// to look up, and it means the resolution happens on the side that can check the address exists.

// Accounts is the slice of the account module this one needs, declared as a consumer-owned port so
// these use cases stay testable against a fake. accountapi.Service satisfies it.
type Accounts interface {
	FindByEmail(ctx context.Context, email string) (accountapi.Account, error)
	Profile(ctx context.Context, accountID string) (accountapi.Account, error)
}

// WithAccounts gives the service its account lookup. Injected after construction because the
// account module publishes its service during Register, and a module may not resolve another's
// during its own — the kernel's staged boot exists so this happens in Start.
func (s *Service) WithAccounts(accounts Accounts) {
	s.accounts = accounts
}

// resolve turns an email address into an account, or says plainly that there is none.
//
// The refusal names the address. An administrator who mistyped one character needs to see which
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

// BootstrapAdmin gives one account the Admin role, by email address.
//
// Without it a fresh deployment is locked out of itself: every module is gated, nobody holds a
// role, and the screen that would grant one is reachable only by somebody who already holds it. The
// first administrator cannot be made by the product, so it is made by configuration.
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
