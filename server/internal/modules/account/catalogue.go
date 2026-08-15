package account

import (
	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	authzapi "__GO_MODULE__/server/internal/modules/authz/api"
)

// The permissions this module enforces in its own handlers.
//
// Only one, and only for the administrative surface: everything under /account/* is about the
// caller's own record and needs no permission beyond being signed in. A permission guarding your
// own profile would be a permission somebody could take away, leaving an account that can sign in
// and then do nothing.

// Permissions declares what this module checks.
func (m *Module) Permissions() []authzapi.Permission {
	return []authzapi.Permission{
		{
			Key:         accountapi.PermissionManageUsers,
			Name:        "Manage Users",
			Description: "Create, edit, delete user accounts and assign roles",
			Category:    "Administration",
			IsHighRisk:  true,

			// The one permission whose row scope is real: Accounts narrows on it — own is
			// yourself, team shares your TeamId, all is every account.
			IsScoped: true,
		},
	}
}

var _ authzapi.Catalogue = (*Module)(nil)
