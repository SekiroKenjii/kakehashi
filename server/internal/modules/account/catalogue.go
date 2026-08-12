package account

import (
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
)

// One permission, and only for the administrative surface: everything under /account/* is about
// the caller's own record and needs no permission beyond being signed in. A permission guarding
// your own profile would be one somebody could take away, leaving an account that can sign in and
// then do nothing.
func (m *Module) Permissions() []authzapi.Permission {
	return []authzapi.Permission{
		{
			Key:         accountapi.PermissionManageUsers,
			Name:        "Manage Users",
			Description: "Create, edit, delete user accounts and assign roles",
			Category:    "Administration",
			IsHighRisk:  true,

			// The one permission in this build whose row scope is real: Accounts narrows on it.
			// own sees only yourself, team sees the accounts sharing your TeamId, all sees every
			// account.
			IsScoped: true,
		},
	}
}

var _ authzapi.Catalogue = (*Module)(nil)
