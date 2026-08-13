// Package service implements the authorization use cases. It is private to the module.
//
// Three files. This one is the seam plus the read the request gate runs on every gated request.
// admin.go holds what an administrator does. bootstrap.go holds what boot does — reconciling the
// catalogue and seeding the roles the product ships.
package service

import (
	"context"
	"time"

	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// Store is the persistence these use cases need, declared here rather than in store/.
type Store interface {
	Permissions(ctx context.Context) ([]domain.Permission, error)
	ReconcilePermissions(ctx context.Context, declared []domain.Permission) error

	Roles(ctx context.Context) ([]domain.Role, error)
	Role(ctx context.Context, id string) (domain.Role, error)
	RoleByName(ctx context.Context, name string) (domain.Role, error)
	InsertRole(ctx context.Context, r domain.Role, at time.Time) error
	UpdateRole(ctx context.Context, r domain.Role) error
	SaveGrants(ctx context.Context, r domain.Role, actorID string, at time.Time) error
	DeleteRole(ctx context.Context, id string) error
	CountsByRole(ctx context.Context) (map[string][2]int, error)

	GrantsOfAccount(ctx context.Context, accountID string) (map[string]string, error)
	RolesOf(ctx context.Context, accountID string) ([]domain.Role, error)
	RolesOfAccounts(ctx context.Context, accountIDs []string) (map[string][]domain.Role, error)
	HoldsPermissionWithoutRole(
		ctx context.Context, accountID, permissionKey, excludedRoleID string) (bool, error)
	AssignRole(ctx context.Context, accountID, roleID, by string, at time.Time) error
	UnassignRole(ctx context.Context, accountID, roleID string) error

	AuditEntries(ctx context.Context, take int) ([]domain.AuditEntry, error)
	InsertAuditEntries(ctx context.Context, entries []domain.AuditEntry) error
}

type (
	// Clock is the service's source of time, injected so a test can pin it.
	Clock func() time.Time

	// IDs is the service's source of identifiers, injected for the same reason as Clock.
	IDs func() string
)

// Service answers what a caller may do, and lets an administrator change it.
type Service struct {
	store    Store
	now      Clock
	newID    IDs
	accounts Accounts
}

// New builds the service. Pass nil for clock or ids to use the wall clock and random UUIDs.
func New(store Store, clock Clock, ids IDs) *Service {
	if clock == nil {
		clock = time.Now
	}
	if ids == nil {
		ids = newUUID
	}
	return &Service{store: store, now: clock, newID: ids}
}

// Resolve is the question the request gate asks, and the only one on the hot path.
//
// It returns every permission the caller holds, each at the widest scope any of their roles gives.
// One query: the widening happens in SQL because the answer is one row per permission either way,
// and pulling every role's every grant back to merge in Go would move the same data and then do
// the work twice.
func (s *Service) Resolve(ctx context.Context, subject auth.Subject) (auth.Grants, error) {
	raw, err := s.store.GrantsOfAccount(ctx, subject.ID)
	if err != nil {
		return nil, err
	}

	grants := make(auth.Grants, len(raw))
	for key, scope := range raw {
		// Widest rather than assignment, even though the query already grouped: a scope the
		// database holds that this build does not recognise must narrow, and Widest is the one
		// place that rule lives.
		grants[key] = auth.Widest(grants[key], auth.Scope(scope))
	}
	return grants, nil
}

// RolesOf lists the roles an account holds, for the account module's user list.
func (s *Service) RolesOf(ctx context.Context, accountID string) ([]authzapi.Role, error) {
	roles, err := s.store.RolesOf(ctx, accountID)
	if err != nil {
		return nil, err
	}
	return toAPIRoles(roles), nil
}

// GrantsForRole resolves one role's grants into the form the request gate speaks.
//
// Not on the hot path, unlike Resolve: nothing authorizes a request with this. It answers "what would
// somebody holding this role be able to do", which is what lets the navigation module draw the pane a
// colleague will see rather than the one the administrator has.
//
// Widest for the reason Resolve gives — a scope stored in the database that this build does not
// recognise has to narrow, and Widest is the one place that rule lives. A role with two grants on the
// same permission is not a shape the admin screen can produce, but the merge costs nothing and means
// this cannot be the code that trusts it.
func (s *Service) GrantsForRole(ctx context.Context, roleID string) (auth.Grants, error) {
	raw, err := s.RoleGrants(ctx, roleID)
	if err != nil {
		return nil, err
	}

	grants := make(auth.Grants, len(raw))
	for _, g := range raw {
		grants[g.PermissionKey] = auth.Widest(grants[g.PermissionKey], auth.Scope(g.Scope))
	}
	return grants, nil
}

func toAPIRoles(roles []domain.Role) []authzapi.Role {
	out := make([]authzapi.Role, len(roles))
	for i, r := range roles {
		out[i] = authzapi.Role{
			ID: r.ID, Name: r.Name, Description: r.Description, IsSystem: r.IsSystem,
		}
	}
	return out
}

var (
	_ authzapi.Service = (*Service)(nil)
	_ auth.Permissions = (*Service)(nil)
)
