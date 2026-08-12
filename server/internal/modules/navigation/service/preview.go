package service

import (
	"context"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Looking at somebody else's pane. Most screens sit behind a permission, so an administrator who
// holds everything is looking at a pane almost nobody else has.

// RoleGrants is declared here rather than taken from authzapi, and satisfied by it: this package
// needs one method, and naming the whole interface would make the navigation module depend on every
// method the authorization module ever adds. It is also what lets a test hand over a map.
type RoleGrants interface {
	GrantsForRole(ctx context.Context, roleID string) (auth.Grants, error)
}

// Set after construction, from Finalize: it comes from another module, and no module may resolve
// another during Register. Optional on purpose — a build without an authorization module has no
// roles to preview, and PreviewFor says so rather than failing at boot.
func (s *Service) WithRoleGrants(grants RoleGrants) {
	s.roleGrants = grants
}

// An empty role means "somebody holding nothing", the useful worst case: would a new colleague see
// anything at all. No permission check here — the whole admin surface is already behind
// navigation.manage, and somebody who may rearrange the pane may look at what they rearranged.
func (s *Service) PreviewFor(ctx context.Context, roleID string) (Pane, error) {
	if roleID == "" {
		// Not a lookup: an account with no roles has no grants, and asking the authorization module
		// to confirm that would be a round trip to be told nothing.
		return s.Build(ctx, auth.Grants{})
	}

	if s.roleGrants == nil {
		return Pane{}, errs.Invalidf(
			"This build has no roles to preview, so there is nothing to look at but your own pane.")
	}

	grants, err := s.roleGrants.GrantsForRole(ctx, roleID)
	if err != nil {
		return Pane{}, err
	}
	return s.Build(ctx, grants)
}
