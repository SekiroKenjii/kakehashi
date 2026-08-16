package navigation

import (
	navigationapi "__GO_MODULE__/server/internal/modules/navigation/api"
)

// The screen this module owns, declared the same way every other module declares its own. Nothing
// here is special-cased: the module that collects the declarations is also one of the modules that
// makes one.

// NavigationDestinations declares the Navigation layout screen.
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
