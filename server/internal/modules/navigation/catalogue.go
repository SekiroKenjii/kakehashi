package navigation

import (
	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// One permission, and only for the layout surface: reading your own pane needs nothing beyond being
// signed in, because a client cannot draw a locked door until it knows the door is there.
func (m *Module) Permissions() []authzapi.Permission {
	return []authzapi.Permission{
		{
			Key:         navigationapi.PermissionManageNavigation,
			Name:        "Arrange navigation",
			Description: "Create headings and decide where each screen sits in the navigation pane",
			Category:    "Administration",
		},
	}
}

var _ authzapi.Catalogue = (*Module)(nil)
