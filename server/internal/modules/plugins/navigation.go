package plugins

import (
	navigationapi "__GO_MODULE__/server/internal/modules/navigation/api"
	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
)

// The screen this module owns.

// NavigationDestinations declares the Plugins screen.
//
// Gated on plugins.manage rather than the module's own access: reading the catalog is something
// every signed-in client does on its own, while the screen is where somebody decides what a fleet
// of machines will run. Hidden rather than disabled when denied, like the other screens whose
// existence is itself administrative.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
	return []navigationapi.Destination{
		{
			ID:             "plugins.library",
			DefaultTitle:   "Plugins",
			DefaultIcon:    "puzzle",
			DefaultGroup:   "administration",
			DefaultOrder:   40,
			Permission:     pluginsapi.PermissionManagePlugins,
			HideWhenDenied: true,
		},
	}
}

var _ navigationapi.Contributor = (*Module)(nil)
