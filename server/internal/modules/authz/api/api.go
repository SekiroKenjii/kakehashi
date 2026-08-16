// Package authzapi is the authorization module's public contract.
//
// Other modules use it to declare the permissions they enforce — the catalogue is assembled from
// those declarations, never typed into a table — and the composition root hands the declarations
// over at mount time.
//
// There is no way to grant here. Deciding who may hold what is this module's alone, and it
// answers the mux through the platform's auth.Permissions port rather than through anything here.
//
// RolesOf and GrantsForRole can be composed into "what may this account do", and nothing in the
// type system stops it. Do not: the route gate is the only component entitled to act on that
// answer, and a module enforcing its own copy is a second authorization decision drifting from
// the first. Read these to explain and to draw, never to decide.
package authzapi

import (
	"context"

	"__GO_MODULE__/server/internal/platform/auth"
)

// Scope names mirror platform/auth's, as plain strings.
//
// These strings cross the wire and are stored on grants; renaming one breaks deployed clients and
// existing grants. They are duplicated rather than imported because an api package importing the
// platform's type would make every consumer of this contract depend on the platform's shape; one
// line in the store keeps them in step.
const (
	ScopeOwn  = "own"
	ScopeTeam = "team"
	ScopeAll  = "all"
)

// Permission is one thing a role may be granted.
type Permission struct {
	// Key is the stable identifier, "<module>.<verb>": users.manage, notes.read. It is what a
	// grant stores and what a handler asks for, so renaming one revokes it everywhere at once.
	Key string

	Name        string
	Description string

	// Category groups the permission on the administration screen — "Administration", "DevOps".
	Category string

	// IsHighRisk marks the ones worth a second look on that screen. Presentation, not policy:
	// nothing behaves differently, a human just reads more carefully.
	IsHighRisk bool

	// IsScoped is a module's promise that the scope on a grant of this permission is honoured —
	// that some store of its own narrows its query on auth.ScopeOf rather than merely asking
	// whether the key is present. Nothing can check the promise. The administration screen offers
	// the own/team/all choice only where IsScoped is true; elsewhere the control is absent rather
	// than inert.
	IsScoped bool
}

// AccessPermission is the key a module's routes are gated on. It delegates to the platform, which
// owns the format.
//
// Three places name this string — the route gate that enforces it, this module that mints it into
// the catalogue, and the navigation module that falls back to it for a screen declaring no
// permission of its own — and two of those are modules that cannot see each other. A copy in each
// would stop matching.
func AccessPermission(moduleID string) string {
	return auth.ModuleAccess(moduleID)
}

// The permissions the authorization module itself enforces, beyond the .access every module gets.
// The keys are stored in role grants and cross the wire; renaming one revokes it everywhere.
//
// Here rather than in the module's own package because its wire layer checks one of them, and a
// package cannot import the package that imports it.
const (
	// PermissionManageRoles guards the whole administrative surface. The one permission that can
	// grant every other one, including itself.
	PermissionManageRoles = "roles.manage"

	// PermissionViewAudit guards the change history.
	PermissionViewAudit = "audit.view"
)

// Catalogue is what a module declares about itself.
//
// A module that declares nothing still gets its .access permission; declaring is only needed for
// the finer ones it checks in its own handlers.
type Catalogue interface {
	Permissions() []Permission
}

// Role is a named set of grants.
type Role struct {
	ID          string
	Name        string
	Description string

	// IsSystem marks the roles the product ships. They can be re-granted but never deleted: a
	// deployment that lost its admin role has no way back in.
	IsSystem bool
}

// Grant is one permission on one role, and how far it reaches.
type Grant struct {
	PermissionKey string
	Scope         string
}

// Service is the read surface other modules may use.
type Service interface {
	// RolesOf lists the roles an account holds.
	RolesOf(ctx context.Context, accountID string) ([]Role, error)

	// GrantsForRole resolves what one role may do, in the form the route gate already speaks.
	//
	// It answers a question about a role rather than about a caller: it grants nothing and
	// authorizes nothing. The navigation module uses it to draw the pane a given role sees.
	//
	// auth.Grants rather than []Grant on purpose: every consumer asks "does this allow X", and a
	// slice would make each caller rebuild the same map and read scopes for itself, which is this
	// module's business.
	GrantsForRole(ctx context.Context, roleID string) (auth.Grants, error)
}
