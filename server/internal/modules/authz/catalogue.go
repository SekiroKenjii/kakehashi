package authz

import authzapi "__GO_MODULE__/server/internal/modules/authz/api"

// The permissions this module enforces in its own handlers, beyond the .access every module gets.
//
// Declared rather than inserted, and declared HERE rather than in a table, because a permission is
// only real if something checks it — and the thing that checks it is in this directory. A catalogue
// entry that outlives its check is a row an administrator can grant and nothing will honour.

// Permissions declares what this module checks. It is what makes the module an authzapi.Catalogue.
func (m *Module) Permissions() []authzapi.Permission {
	return []authzapi.Permission{
		{
			Key:         authzapi.PermissionManageRoles,
			Name:        "Manage Roles",
			Description: "Create and modify roles and their permission sets",
			Category:    "Administration",
			// The one permission that can grant every other one, including itself. Everything else
			// on this screen is reachable by somebody who holds it.
			IsHighRisk: true,
		},
		{
			Key:         authzapi.PermissionViewAudit,
			Name:        "View Audit Log",
			Description: "Read system audit trail and change history",
			Category:    "Administration",
		},
	}
}

// The keys live in the api package: the module's own wire layer checks one of them, and it cannot
// import this package without a cycle.

var _ authzapi.Catalogue = (*Module)(nil)
