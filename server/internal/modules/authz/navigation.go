package authz

import (
	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// Its own permission, for the same reason the module's routes are not gated on authz.access:
// nobody holds that key, so a destination falling back to it would be locked for everybody.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
	return []navigationapi.Destination{
		{
			ID:             "authz.roles",
			DefaultTitle:   "Role permissions",
			DefaultIcon:    "permissions",
			DefaultGroup:   "administration",
			DefaultOrder:   20,
			Permission:     authzapi.PermissionManageRoles,
			HideWhenDenied: true,
		},
	}
}

var _ navigationapi.Contributor = (*Module)(nil)
