package notes

import (
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// No Permission, so the module's own notes.access applies: the pane's lock and the server's refusal
// read the same row, and a screen is locked exactly when its endpoints are.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
	return []navigationapi.Destination{
		{
			ID:           "notes",
			DefaultTitle: "Notes",
			DefaultIcon:  "note",
			DefaultGroup: "utilities",
			DefaultOrder: 10,
		},
	}
}

var _ navigationapi.Contributor = (*Module)(nil)
