// Package authzapi is the authorization module's public contract.
//
// Modules use it to declare the permissions they enforce: a catalogue entry no module claims is a
// permission nothing checks, so the catalogue is assembled from declarations rather than typed
// into a table.
//
// Note what is absent: no way to grant. Deciding who may hold what is this module's alone, and it
// answers the mux through the platform's auth.Permissions port rather than through anything here.
//
// RolesOf and GrantsForRole can be composed into "what may this account do", and nothing in the
// type system stops it. Only the route gate is entitled to act on that answer — a module that
// computed its own and enforced it would be a second authorization decision, drifting from the
// first. Read these to explain and to draw. Do not read them to decide.
package authzapi

import (
	"context"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// Duplicated from platform/auth rather than imported: the platform owns the algebra, this owns the
// vocabulary, and an api package importing the platform's type makes every consumer of this
// contract depend on the platform's shape.
const (
	ScopeOwn  = "own"
	ScopeTeam = "team"
	ScopeAll  = "all"
)

type Permission struct {
	// "<module>.<verb>": users.manage, notes.read. What a grant stores and what a handler asks
	// for, so renaming one revokes it everywhere at once.
	Key string

	Name        string
	Description string

	// Groups the permission on the administration screen — "Administration", "DevOps".
	Category string

	// Presentation, not policy: nothing behaves differently, a human just reads more carefully.
	IsHighRisk bool

	// A module's promise that the scope on a grant of this permission is honoured — that some
	// store of its own narrows its query on auth.ScopeOf rather than merely asking whether the key
	// is present. A promise rather than a description, because nothing can check it. What it buys
	// is that the administration screen offers the own/team/all choice only where choosing changes
	// an answer; everywhere else the control is absent rather than inert.
	IsScoped bool
}

// The key a module's routes are gated on. Three places name this string — the route gate that
// enforces it, this module that mints it into the catalogue, and the navigation module that falls
// back to it for a screen declaring no permission of its own — and two of those cannot see each
// other, so the format lives in the platform and everyone delegates.
func AccessPermission(moduleID string) string {
	return auth.ModuleAccess(moduleID)
}

// Here rather than in the module's own package because its wire layer checks one of them, and a
// package cannot import the package that imports it.
const (
	// Guards the whole administrative surface. The one permission that can grant every other one,
	// including itself.
	PermissionManageRoles = "roles.manage"

	PermissionViewAudit = "audit.view"
)

// A module that declares nothing still gets its .access permission; declaring is only needed for
// the finer ones it checks in its own handlers.
type Catalogue interface {
	Permissions() []Permission
}

type Role struct {
	ID          string
	Name        string
	Description string

	// The roles the product ships. They can be re-granted but never deleted: a deployment that
	// lost its admin role has no way back in.
	IsSystem bool
}

type Grant struct {
	PermissionKey string
	Scope         string
}

type Service interface {
	RolesOf(ctx context.Context, accountID string) ([]Role, error)

	// Answers about a role rather than about a caller, which is what makes it useful to somebody
	// arranging a screen for other people: the navigation module draws a pane with it, so an
	// administrator can see what a colleague will see instead of guessing from their own.
	//
	// auth.Grants rather than []Grant on purpose. Every consumer wants to ask "does this allow X",
	// and a slice would make each of them rebuild the same map — and decide for itself how to read
	// a scope, which is this module's business.
	GrantsForRole(ctx context.Context, roleID string) (auth.Grants, error)
}
