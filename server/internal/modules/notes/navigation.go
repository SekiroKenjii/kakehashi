package notes

import (
	navigationapi "__GO_MODULE__/server/internal/modules/navigation/api"
)

// The screen this module owns.

// NavigationDestinations declares the Notes page.
//
// No Permission, which means the module's own notes.access — the ordinary case, and the one worth
// keeping ordinary: the pane's lock and the server's refusal then read the same row, so a screen is
// locked exactly when its endpoints are.
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
