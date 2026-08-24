package plugins

import (
	authzapi "__GO_MODULE__/server/internal/modules/authz/api"
	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
)

// The permission this module enforces in its own handlers.

// Permissions declares what this module checks.
//
// Its own permission rather than reusing roles.manage: publishing a package every client will
// execute is a different job from handing out access, and the set of people trusted with the first
// is smaller. High-risk for the same reason — what lands in this catalog runs with the whole of the
// application's privileges on somebody else's machine.
func (m *Module) Permissions() []authzapi.Permission {
	return []authzapi.Permission{
		{
			Key:         pluginsapi.PermissionManagePlugins,
			Name:        "Publish plugins",
			Description: "Publish and withdraw the plugin packages this deployment offers",
			Category:    "Administration",
			IsHighRisk:  true,
		},
	}
}

var _ authzapi.Catalogue = (*Module)(nil)
