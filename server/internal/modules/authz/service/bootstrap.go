package service

import (
	"context"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// What boot does: make the catalogue match what the modules declared, and make sure the roles the
// product ships exist.
//
// Both are reconciliations rather than migrations. A migration states a change and runs once; these
// state what should be true and run every boot, which is what lets a module be added, removed or
// renamed without anyone hand-writing SQL to match.

// SystemRoles are the roles the product ships.
//
// Named here rather than seeded by SQL because a migration that inserted them could not be re-run
// after somebody renamed one, and because this list is the answer to "what does a fresh deployment
// look like" — a question worth being able to read in one place.
//
// Only Administrator carries grants at boot, and it carries all of them. The rest ship empty on
// purpose: a starter role that already grants things is a role nobody reviews, and the whole point
// of the administration screen is that somebody decides.
var SystemRoles = []struct {
	Name        string
	Description string
}{
	{"Admin", "Full system access"},
	{"Developer", "DevOps + infrastructure access"},
	{"Operations", "Monitoring + read-only ops"},
	{"Viewer", "Read-only across all modules"},
	{"Guest", "Minimal access · temporary users"},
}

// AdminRoleName is the role that starts with every permission, and the one an administrator must
// hold to reach the administration screens.
const AdminRoleName = "Admin"

// Reconcile makes the stored catalogue match what the modules declared, then ensures the system
// roles exist and that Admin holds everything.
//
// Admin is re-granted every boot rather than only when created. That is deliberate: adding a module
// adds permissions, and an Admin role that did not gain them would leave a deployment with no
// account able to grant them to anybody — a lockout with no way back that would only show up the
// first time somebody needed the new module.
func (s *Service) Reconcile(ctx context.Context, declared []domain.Permission) error {
	if err := s.store.ReconcilePermissions(ctx, declared); err != nil {
		return err
	}

	for _, wanted := range SystemRoles {
		if err := s.ensureRole(ctx, wanted.Name, wanted.Description); err != nil {
			return err
		}
	}

	return s.grantEverythingToAdmin(ctx, declared)
}

func (s *Service) ensureRole(ctx context.Context, name, description string) error {
	_, err := s.store.RoleByName(ctx, name)
	if err == nil {
		// Left alone once it exists. An administrator who renamed the description meant it, and a
		// boot that overwrote their edit every restart would be its own kind of bug.
		return nil
	}
	if errs.KindOf(err) != errs.NotFound {
		return err
	}

	role, err := domain.NewRole(s.newID(), name, description, true)
	if err != nil {
		return err
	}
	return s.store.InsertRole(ctx, role, s.now())
}

func (s *Service) grantEverythingToAdmin(ctx context.Context, declared []domain.Permission) error {
	admin, err := s.store.RoleByName(ctx, AdminRoleName)
	if err != nil {
		return err
	}

	admin.Grants = make(map[string]string, len(declared))
	for _, p := range declared {
		// At the widest scope, because the row-level half of a permission is meaningless for the
		// role whose job is to see everything — and an Admin who could only see their own rows
		// could not audit anybody.
		if err := admin.Grant(p.Key, domain.ScopeAll); err != nil {
			return err
		}
	}

	return s.store.SaveGrants(ctx, admin, "system", s.now())
}
