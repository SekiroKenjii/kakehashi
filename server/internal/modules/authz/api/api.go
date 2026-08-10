// Package authzapi is the authorization module's public contract.
//
// Two audiences, and the split between them is the point. Other MODULES use it to declare the
// permissions they enforce — a catalogue entry no module claims is a permission nothing checks, so
// the catalogue is assembled from declarations rather than typed into a table. The composition root
// uses it to hand those declarations over at mount time.
//
// Note what is absent: no way to grant, and no way to ask about somebody else. Deciding who may do
// what is this module's alone, and it answers the mux through the platform's auth.Permissions port
// rather than through anything here.
package authzapi

import (
	"context"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// Scope names mirror platform/auth's, as plain strings.
//
// They are duplicated rather than imported because this is a module's api package and the values
// cross the wire; the platform owns the algebra, this owns the vocabulary. One line in the store
// keeps them in step, and the alternative — an api package importing the platform's type — makes
// every consumer of this contract depend on the platform's shape.
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

	// IsScoped is a module's promise that the scope on a grant of this permission is actually
	// honoured — that some store of its own narrows its query on auth.ScopeOf rather than merely
	// asking whether the key is present.
	//
	// It is a promise rather than a description because nothing can check it. What it buys is that
	// the administration screen offers the own/team/all choice only where choosing changes an
	// answer; everywhere else the control is absent rather than inert.
	IsScoped bool
}

// AccessPermission is the key a module's routes are gated on.
//
// It delegates to the platform, which is where the format lives now. Three places name this string
// — the route gate that enforces it, this module that mints it into the catalogue, and the
// navigation module that falls back to it for a screen declaring no permission of its own — and two
// of those are modules that cannot see each other. A copy each is a copy that stops matching.
func AccessPermission(moduleID string) string {
	return auth.ModuleAccess(moduleID)
}

// The permissions the authorization module itself enforces, beyond the .access every module gets.
//
// Here rather than in the module's own package because its wire layer checks one of them, and a
// package cannot import the package that imports it. An api package is where a module puts what
// something else has to name.
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
}
