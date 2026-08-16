package authz

import (
	authzapi "__GO_MODULE__/server/internal/modules/authz/api"
	navigationapi "__GO_MODULE__/server/internal/modules/navigation/api"
)

// The screen this module owns.

// NavigationDestinations declares the Role permissions screen.
//
// Its own permission, for the same reason the module's routes are not gated on authz.access: a
// module that answers "what may I do" cannot require permission to answer, so nobody holds that key
// and a destination falling back to it would be locked for everybody.
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
