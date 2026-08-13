package service

import (
	"context"
	"fmt"

	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// What an administrator does. A different caller from the read in service.go, on a surface the
// module mounts behind one permission check.

// Actor is who is making the change, carried together because the audit trail records both the
// id and the name it had at the time. Two parameters that must not drift apart are one type.
type Actor struct {
	ID   string
	Name string
}

// RoleSummary is a role plus the two counts the list screen shows.
type RoleSummary struct {
	Role            authzapi.Role
	PermissionCount int
	AccountCount    int
}

// Roles lists every role with its counts, and how many permissions exist in total.
//
// The total comes back with the list so the screen can render "22 / 34" without a second call —
// a number every row needs and no row owns.
func (s *Service) Roles(ctx context.Context) ([]RoleSummary, int, error) {
	roles, err := s.store.Roles(ctx)
	if err != nil {
		return nil, 0, err
	}
	counts, err := s.store.CountsByRole(ctx)
	if err != nil {
		return nil, 0, err
	}
	permissions, err := s.store.Permissions(ctx)
	if err != nil {
		return nil, 0, err
	}

	out := make([]RoleSummary, len(roles))
	for i, r := range roles {
		c := counts[r.ID]
		out[i] = RoleSummary{
			Role: authzapi.Role{
				ID: r.ID, Name: r.Name, Description: r.Description, IsSystem: r.IsSystem,
			},
			PermissionCount: c[0],
			AccountCount:    c[1],
		}
	}
	return out, len(permissions), nil
}

// Permissions returns the whole catalogue, ordered by category then name.
func (s *Service) Permissions(ctx context.Context) ([]domain.Permission, error) {
	return s.store.Permissions(ctx)
}

// RoleGrants returns one role's grants.
func (s *Service) RoleGrants(ctx context.Context, roleID string) ([]authzapi.Grant, error) {
	role, err := s.store.Role(ctx, roleID)
	if err != nil {
		return nil, err
	}

	out := make([]authzapi.Grant, 0, len(role.Grants))
	for key, scope := range role.Grants {
		out = append(out, authzapi.Grant{PermissionKey: key, Scope: scope})
	}
	return out, nil
}

// SaveResult is what changed, so the screen and the audit trail can agree.
type SaveResult struct {
	Granted  int
	Revoked  int
	Rescoped int
}

// ensureActorKeepsControl refuses a change that would take roles.manage away from whoever is
// making it.
//
// The failure it prevents is two clicks deep and unrecoverable through the product: an
// administrator switches roles.manage off on the role they themselves hold, saves, and the next
// request — including every request this screen makes — is refused. Nothing in the UI can undo it,
// because undoing it needs the permission that was just removed. The only way back is the
// environment variable that seeds the first administrator, i.e. a redeploy.
//
// Checked here rather than in the client, because a client check is a hidden button and this is a
// rule. Somebody else may still remove it — that is an administrator being demoted by a colleague,
// which is ordinary and leaves a second person able to put it back.
func (s *Service) ensureActorKeepsControl(
	ctx context.Context, actorID, roleID string, roleStillGrantsIt bool,
) error {
	if roleStillGrantsIt {
		return nil
	}

	// Excluding the role being changed answers the question for both cases at once: an actor who
	// does not hold this role is unaffected by excluding it, and so passes.
	elsewhere, err := s.store.HoldsPermissionWithoutRole(
		ctx, actorID, authzapi.PermissionManageRoles, roleID)
	if err != nil {
		return err
	}
	if elsewhere {
		return nil
	}

	return errs.Invalidf(
		"This would remove your own %s permission, and nothing in the app could give it back. "+
			"Ask another administrator to make this change.",
		authzapi.PermissionManageRoles)
}

// SaveRoleGrants replaces a role's entire grant set.
//
// The whole set rather than a diff, because that is what the screen has. The diff is computed HERE
// rather than sent, for two reasons: the audit trail wants to record what actually changed, and a
// client that computed it could be wrong about the starting state — somebody else may have saved
// while this administrator was deciding.
func (s *Service) SaveRoleGrants(
	ctx context.Context, roleID string, wanted []authzapi.Grant, actor Actor,
) (SaveResult, error) {
	role, err := s.store.Role(ctx, roleID)
	if err != nil {
		return SaveResult{}, err
	}

	// Checked against the catalogue the modules declare: a key absent from it enforces nothing, and
	// reconciliation prunes the catalogue and never the grants, so the row survives forever.
	catalogue, err := s.store.Permissions(ctx)
	if err != nil {
		return SaveResult{}, err
	}
	known := make(map[string]struct{}, len(catalogue))
	for _, p := range catalogue {
		known[p.Key] = struct{}{}
	}

	before := role.Grants
	role.Grants = map[string]string{}
	for _, g := range wanted {
		if _, ok := known[g.PermissionKey]; !ok {
			return SaveResult{}, errs.Invalidf(
				"No permission called %s is enforced by this server.", g.PermissionKey)
		}
		if err := role.Grant(g.PermissionKey, g.Scope); err != nil {
			return SaveResult{}, err
		}
	}

	_, keepsControl := role.Grants[authzapi.PermissionManageRoles]
	if err := s.ensureActorKeepsControl(ctx, actor.ID, roleID, keepsControl); err != nil {
		return SaveResult{}, err
	}

	result, entries := s.diff(role, before)
	if err := s.store.SaveGrants(ctx, role, actor.ID, s.now()); err != nil {
		return SaveResult{}, err
	}

	// After the save, never before: an entry describing a change that failed is read as fact. Its
	// own failure is swallowed — losing the record must not undo the change.
	if len(entries) > 0 {
		s.record(ctx, actor, role, entries)
	}
	return result, nil
}

// diff works out what moved between two grant sets, and the audit entries describing it.
func (s *Service) diff(role domain.Role, before map[string]string) (SaveResult, []auditChange) {
	var result SaveResult
	var changes []auditChange

	for key, scope := range role.Grants {
		switch old, had := before[key]; {
		case !had:
			result.Granted++
			changes = append(changes, auditChange{domain.ActionGranted, key, "scope " + scope})
		case old != scope:
			result.Rescoped++
			changes = append(changes, auditChange{
				domain.ActionRescoped, key, fmt.Sprintf("scope %s to %s", old, scope)})
		}
	}
	for key := range before {
		if _, kept := role.Grants[key]; !kept {
			result.Revoked++
			changes = append(changes, auditChange{domain.ActionRevoked, key, ""})
		}
	}
	return result, changes
}

type auditChange struct {
	action        string
	permissionKey string
	detail        string
}

func (s *Service) record(
	ctx context.Context, actor Actor, role domain.Role, changes []auditChange,
) {
	at := s.now()
	actor.Name = s.nameOf(ctx, actor)
	entries := make([]domain.AuditEntry, len(changes))
	for i, c := range changes {
		entries[i] = domain.AuditEntry{
			ID:         s.newID(),
			OccurredAt: at,
			ActorID:    actor.ID,
			ActorName:  actor.Name,
			Action:     c.action,
			RoleID:     role.ID,
			// The name at the time, so a trail read after a rename — or after the role is gone —
			// still says what the administrator saw.
			RoleName:      role.Name,
			PermissionKey: c.permissionKey,
			Detail:        c.detail,
		}
	}
	_ = s.store.InsertAuditEntries(ctx, entries)
}

// nameOf fills in the actor's display name when the token did not carry one.
//
// One lookup per administrative action, which is a rate a person types at. The alternative is an
// audit trail whose actor column is a UUID — technically complete, and unreadable by the person
// the trail exists for.
func (s *Service) nameOf(ctx context.Context, actor Actor) string {
	if actor.Name != "" || s.accounts == nil {
		return actor.Name
	}

	account, err := s.accounts.Profile(ctx, actor.ID)
	if err != nil {
		// The id, rather than nothing. A row saying who it cannot name still says there was a who.
		return actor.ID
	}
	if account.DisplayName != "" {
		return account.DisplayName
	}
	return account.Email
}

// CreateRole adds a role, optionally copying another's grants.
func (s *Service) CreateRole(
	ctx context.Context, name, description, cloneFrom string, actor Actor,
) (authzapi.Role, error) {
	if _, err := s.store.RoleByName(ctx, name); err == nil {
		return authzapi.Role{}, errs.Conflictf("A role named %q already exists.", name)
	} else if errs.KindOf(err) != errs.NotFound {
		return authzapi.Role{}, err
	}

	role, err := domain.NewRole(s.newID(), name, description, false)
	if err != nil {
		return authzapi.Role{}, err
	}

	if cloneFrom != "" {
		source, err := s.store.Role(ctx, cloneFrom)
		if err != nil {
			return authzapi.Role{}, err
		}
		for key, scope := range source.Grants {
			if err := role.Grant(key, scope); err != nil {
				return authzapi.Role{}, err
			}
		}
	}

	if err := s.store.InsertRole(ctx, role, s.now()); err != nil {
		return authzapi.Role{}, err
	}

	s.record(ctx, actor, role, []auditChange{{domain.ActionRoleCreated, "", ""}})
	return authzapi.Role{
		ID: role.ID, Name: role.Name, Description: role.Description, IsSystem: role.IsSystem,
	}, nil
}

// UpdateRole renames a role or rewrites its description. The domain decides what a system role
// may change; this only persists the outcome and records who asked.
func (s *Service) UpdateRole(
	ctx context.Context, roleID, name, description string, actor Actor,
) (authzapi.Role, error) {
	role, err := s.store.Role(ctx, roleID)
	if err != nil {
		return authzapi.Role{}, err
	}

	before := role.Name
	if err := role.Rename(name, description); err != nil {
		return authzapi.Role{}, err
	}

	if role.Name != before {
		// The same pre-check CreateRole makes, for the same message.
		if _, err := s.store.RoleByName(ctx, role.Name); err == nil {
			return authzapi.Role{}, errs.Conflictf("A role named %q already exists.", role.Name)
		} else if errs.KindOf(err) != errs.NotFound {
			return authzapi.Role{}, err
		}
	}

	if err := s.store.UpdateRole(ctx, role); err != nil {
		return authzapi.Role{}, err
	}

	detail := ""
	if before != role.Name {
		// The prior name, because after a rename it exists nowhere else to be looked up.
		detail = "was " + before
	}
	s.record(ctx, actor, role, []auditChange{{domain.ActionRoleEdited, "", detail}})
	return authzapi.Role{
		ID: role.ID, Name: role.Name, Description: role.Description, IsSystem: role.IsSystem,
	}, nil
}

// DeleteRole removes a role. Its grants and assignments go with it, by cascade.
func (s *Service) DeleteRole(ctx context.Context, roleID string, actor Actor) error {
	role, err := s.store.Role(ctx, roleID)
	if err != nil {
		return err
	}
	if err := role.CanDelete(); err != nil {
		return err
	}
	// Deleting a role takes its grants with it, so it can lock the actor out exactly as a save can.
	if err := s.ensureActorKeepsControl(ctx, actor.ID, roleID, false); err != nil {
		return err
	}

	if err := s.store.DeleteRole(ctx, roleID); err != nil {
		return err
	}

	// Recorded with the name, because after this there is no row to look it up in.
	s.record(ctx, actor, role, []auditChange{{domain.ActionRoleDeleted, "", ""}})
	return nil
}

// AssignRole gives an account a role, naming the account by email.
func (s *Service) AssignRole(ctx context.Context, email, roleID string, actor Actor) error {
	account, err := s.resolve(ctx, email)
	if err != nil {
		return err
	}
	role, err := s.store.Role(ctx, roleID)
	if err != nil {
		return err
	}

	if err := s.store.AssignRole(ctx, account.ID, roleID, actor.ID, s.now()); err != nil {
		return err
	}
	s.record(ctx, actor, role, []auditChange{{domain.ActionRoleAssigned, "", email}})
	return nil
}

// UnassignRole takes a role away.
func (s *Service) UnassignRole(ctx context.Context, email, roleID string, actor Actor) error {
	account, err := s.resolve(ctx, email)
	if err != nil {
		return err
	}
	role, err := s.store.Role(ctx, roleID)
	if err != nil {
		return err
	}

	if account.ID == actor.ID {
		// Only when it is your own. Another administrator unassigning you is ordinary, and leaves
		// somebody able to put it back.
		if err := s.ensureActorKeepsControl(ctx, actor.ID, roleID, false); err != nil {
			return err
		}
	}

	if err := s.store.UnassignRole(ctx, account.ID, roleID); err != nil {
		return err
	}
	s.record(ctx, actor, role, []auditChange{{domain.ActionRoleUnassigned, "", email}})
	return nil
}

// RolesOfAccounts answers for many accounts at once, so the user list runs one query rather than
// one per row.
func (s *Service) RolesOfAccounts(
	ctx context.Context, accountIDs []string,
) (map[string][]authzapi.Role, error) {
	byAccount, err := s.store.RolesOfAccounts(ctx, accountIDs)
	if err != nil {
		return nil, err
	}

	out := make(map[string][]authzapi.Role, len(byAccount))
	for id, roles := range byAccount {
		out[id] = toAPIRoles(roles)
	}
	return out, nil
}

// AuditEntries returns the most recent changes, newest first.
func (s *Service) AuditEntries(ctx context.Context, take int) ([]domain.AuditEntry, error) {
	if take <= 0 || take > 200 {
		// Clamped rather than rejected: the parameter comes off the wire, and a client asking for
		// a million rows deserves an answer rather than an error.
		take = 50
	}
	return s.store.AuditEntries(ctx, take)
}
