// Package domain holds the authorization module's aggregate and the rules it enforces.
//
// One root: Role. It is a root rather than a record because it has a consistency boundary worth
// naming — a role and its grants change together, an administrator saves eight toggles as one act,
// and a grant with no role is unreachable. The grants are entities inside it, which is why they
// have no file of their own and why the store writes them in one transaction.
//
// Permission is here too, as a value object rather than a root: it is a catalogue the modules
// declare and the boot reconciles, not something a user creates, so it defends no invariant.
// It lives here rather than in store/ because the service names it as often as the store does.
package domain

import (
	"strings"
	"unicode/utf8"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// MaxRoleName is the longest a role's name may be, matching the column it is stored in.
const MaxRoleName = 64

// Permission is one thing a role may be granted. The catalogue is assembled from what the modules
// declare, so this type carries description rather than rules.
type Permission struct {
	// Key is the stable identifier, "<module>.<verb>". Renaming one revokes it everywhere at once.
	Key string

	Name        string
	Description string

	// Category groups the permission on the administration screen.
	Category string

	// IsHighRisk is presentation, not policy: nothing behaves differently, a human reads more
	// carefully.
	IsHighRisk bool

	// IsScoped means the module that enforces this permission narrows its own queries on the
	// grant's scope. Where it is false, own/team/all would be a control with no consequence.
	IsScoped bool
}

// Role is a named set of grants.
type Role struct {
	ID          string
	Name        string
	Description string

	// IsSystem marks a role the product ships. It may be re-granted freely; it may not be deleted.
	IsSystem bool

	// Grants is the set, keyed by permission so a role cannot hold one permission twice at two
	// scopes — which is the shape that makes "what does this role allow?" unanswerable.
	Grants map[string]string
}

// NewRole builds a role, rejecting what could not be shown or referred to.
func NewRole(id, name, description string, isSystem bool) (Role, error) {
	trimmed := strings.TrimSpace(name)
	if id == "" {
		return Role{}, errs.Invalidf("A role needs an id.")
	}
	if trimmed == "" {
		return Role{}, errs.Invalidf("A role needs a name.")
	}
	// Runes, not bytes. len() counts UTF-8 bytes, so "Quản trị hệ thống nhân sự" — twenty-five
	// characters — measured well over the limit and was refused with a message about characters.
	// The column is nvarchar(64), which counts UTF-16 units, so runes are the closer of the two and
	// the one the message means.
	if utf8.RuneCountInString(trimmed) > MaxRoleName {
		return Role{}, errs.Invalidf("A role name is limited to %d characters.", MaxRoleName)
	}

	return Role{
		ID:          id,
		Name:        trimmed,
		Description: strings.TrimSpace(description),
		IsSystem:    isSystem,
		Grants:      map[string]string{},
	}, nil
}

// Grant adds or re-scopes a permission on this role.
//
// Re-granting at a different scope replaces rather than accumulates: a role holding one permission
// at two scopes has no answer to "how far does this reach", and the widening rule that resolves
// two ROLES cannot help inside one.
func (r *Role) Grant(permissionKey, scope string) error {
	key := strings.TrimSpace(permissionKey)
	if key == "" {
		return errs.Invalidf("A grant needs a permission.")
	}
	if !IsScope(scope) {
		return errs.Invalidf("%q is not a scope. Use own, team or all.", scope)
	}

	if r.Grants == nil {
		r.Grants = map[string]string{}
	}
	r.Grants[key] = scope
	return nil
}

// Rename changes what the role is called and how it is described.
//
// A system role keeps its name. Boot reconciles the shipped roles BY name — renaming Admin would
// have the next boot recreate "Admin" beside the renamed one, and the bootstrap grant would land
// on the empty twin. Its description may change: nothing resolves by it.
func (r *Role) Rename(name, description string) error {
	trimmed := strings.TrimSpace(name)
	if trimmed == "" {
		return errs.Invalidf("A role needs a name.")
	}
	if len(trimmed) > 64 {
		return errs.Invalidf("A role name is limited to 64 characters.")
	}
	if r.IsSystem && trimmed != r.Name {
		return errs.Invalidf("%q ships with the product and keeps its name.", r.Name)
	}

	r.Name = trimmed
	r.Description = strings.TrimSpace(description)
	return nil
}

// Revoke removes a permission from this role. Revoking one it does not hold succeeds.
func (r *Role) Revoke(permissionKey string) {
	delete(r.Grants, strings.TrimSpace(permissionKey))
}

// CanDelete reports whether this role may be removed.
//
// Returned as a Result-shaped error rather than a bool so the refusal carries its reason to the
// screen: "you cannot delete this" without saying why is the least useful sentence in software.
func (r Role) CanDelete() error {
	if r.IsSystem {
		return errs.Invalidf("%q ships with the product and cannot be deleted.", r.Name)
	}
	return nil
}

// The scopes, as the domain names them. platform/auth owns the algebra that ranks them; these are
// the values this module validates and stores.
const (
	ScopeOwn  = "own"
	ScopeTeam = "team"
	ScopeAll  = "all"
)

// IsScope reports whether s is a scope this build understands.
//
// Validated at the door rather than at the point of use: an unrecognised scope stored in the
// database is a grant whose reach nobody can state, and it will be read long after whoever typed
// it has gone.
func IsScope(s string) bool {
	switch s {
	case ScopeOwn, ScopeTeam, ScopeAll:
		return true
	default:
		return false
	}
}
