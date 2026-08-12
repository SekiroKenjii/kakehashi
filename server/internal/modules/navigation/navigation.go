package navigation

import (
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// Declared the same way every other module declares its own: the module that collects the
// declarations is not special-cased.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
	return []navigationapi.Destination{
		{
			ID:             "navigation.layout",
			DefaultTitle:   "Navigation",
			DefaultIcon:    "navigation",
			DefaultGroup:   "administration",
			DefaultOrder:   30,
			Permission:     navigationapi.PermissionManageNavigation,
			HideWhenDenied: true,
		},
	}
}

var _ navigationapi.Contributor = (*Module)(nil)
