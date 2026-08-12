package account

import (
	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// Only the administrative user directory. The caller's own Account page is deliberately absent: it
// is a footer item the client places itself, so nothing about it is a deployment's to arrange.
//
// The destination names its own permission because this module's routes are not gated on
// account.access — signing in cannot require a permission you can only have after signing in — so
// falling back to that key would draw a row disabled for everybody, forever.
//
// HideWhenDenied, because the existence of a user directory is itself administrative: a locked row
// on every screen tells an ordinary account nothing it can act on.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
	return []navigationapi.Destination{
		{
			ID:             "account.users",
			DefaultTitle:   "Users",
			DefaultIcon:    "people",
			DefaultGroup:   "administration",
			DefaultOrder:   10,
			Permission:     accountapi.PermissionManageUsers,
			HideWhenDenied: true,
		},
	}
}

var _ navigationapi.Contributor = (*Module)(nil)
