package navigation

import (
	authzapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/api"
	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// The permission this module enforces in its own handlers.
//
// Only one, and only for the layout surface: reading your own pane needs nothing beyond being signed
// in, because a client cannot draw a locked door until it knows the door is there.

// Permissions declares what this module checks.
//
// Its own permission rather than reusing roles.manage: arranging a pane and handing out access are
// different jobs, and somebody trusted to tidy the navigation need not be trusted to grant
// permissions. Not high-risk — the worst a mistake here can do is hide something, which is the whole
// reason the layout is safe to hand over at runtime.
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
