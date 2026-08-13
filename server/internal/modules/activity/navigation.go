package activity

import (
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// NavigationDestinations declares the Activity page, gated on this module's activity.access.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
	return []navigationapi.Destination{
		{
			ID:           "activity",
			DefaultTitle: "Activity",
			DefaultIcon:  "activity",
			DefaultGroup: "utilities",
			DefaultOrder: 20,
		},
	}
}

var _ navigationapi.Contributor = (*Module)(nil)
