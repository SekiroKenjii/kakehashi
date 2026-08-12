// Package domain holds the authorization module's aggregate and the rules it enforces.
//
// One root: Role. A root rather than a record because a role and its grants change together — an
// administrator saves eight toggles as one act, and a grant with no role is unreachable. The
// grants are entities inside it, which is why they have no file of their own and why the store
// writes them in one transaction.
//
// Permission is a value object rather than a root: a catalogue the modules declare and boot
// reconciles, not something a user creates, so it defends no invariant. Here rather than in store/
// because the service names it as often as the store does.
package domain

import (
	"strings"
	"unicode/utf8"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// Matches the column the name is stored in.
const MaxRoleName = 64

// The catalogue is assembled from what the modules declare, so this type carries description
// rather than rules.
type Permission struct {
	// "<module>.<verb>". Renaming one revokes it everywhere at once.
	Key string

	Name        string
	Description string

	Category string

	// Presentation, not policy: nothing behaves differently, a human reads more carefully.
	IsHighRisk bool

	// The module that enforces this permission narrows its own queries on the grant's scope. Where
	// it is false, own/team/all would be a control with no consequence.
	IsScoped bool
}

type Role struct {
	ID          string
	Name        string
	Description string

	// A role the product ships. It may be re-granted freely; it may not be deleted.
	IsSystem bool

	// Keyed by permission so a role cannot hold one permission twice at two scopes — the shape
	// that makes "what does this role allow?" unanswerable.
	Grants map[string]string
}

func NewRole(id, name, description string, isSystem bool) (Role, error) {
	trimmed := strings.TrimSpace(name)
	if id == "" {
		return Role{}, errs.Invalidf("A role needs an id.")
	}
	if trimmed == "" {
		return Role{}, errs.Invalidf("A role needs a name.")
	}
	// Runes, not bytes: len() counts UTF-8 bytes, so "Quản trị hệ thống nhân sự" — twenty-five
	// characters — measured over the limit and was refused with a message about characters. The
	// column is nvarchar(64), which counts UTF-16 units, so runes are the closer of the two.
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

// Re-granting at a different scope replaces rather than accumulates: a role holding one permission
// at two scopes has no answer to "how far does this reach", and the widening rule that resolves two
// roles cannot help inside one.
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

// A system role keeps its name. Boot reconciles the shipped roles by name — renaming Admin would
// have the next boot recreate "Admin" beside the renamed one, and the bootstrap grant would land on
// the empty twin. Its description may change: nothing resolves by it.
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

func (r *Role) Revoke(permissionKey string) {
	delete(r.Grants, strings.TrimSpace(permissionKey))
}

// An error rather than a bool so the refusal carries its reason to the screen: "you cannot delete
// this" without saying why is the least useful sentence in software.
func (r Role) CanDelete() error {
	if r.IsSystem {
		return errs.Invalidf("%q ships with the product and cannot be deleted.", r.Name)
	}
	return nil
}

// platform/auth owns the algebra that ranks these; this module only validates and stores them.
const (
	ScopeOwn  = "own"
	ScopeTeam = "team"
	ScopeAll  = "all"
)

// Validated at the door rather than at the point of use: an unrecognised scope stored in the
// database is a grant whose reach nobody can state, and it will be read long after whoever typed it
// has gone.
func IsScope(s string) bool {
	switch s {
	case ScopeOwn, ScopeTeam, ScopeAll:
		return true
	default:
		return false
	}
}
