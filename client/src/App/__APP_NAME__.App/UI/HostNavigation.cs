using System.Collections.Generic;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.UI.Contracts;

namespace __ROOT_NAMESPACE__.App.UI;

/// <summary>
/// The destinations the host owns, in the same shape a module contributes.
/// </summary>
/// <remarks>
/// The administration screens are not a feature module — they govern every module, and a module
/// that governed the others would reach across the boundary the architecture tests hold. But
/// they are still pane items: declaring them here lets the same planner group, order and gate
/// them, and gives the shell exactly one list to render.
/// </remarks>
public static class HostNavigation
{
    /// <summary>
    /// Home is not here: it is the one fixed destination, and the shell owns it.
    /// </summary>
    /// <remarks>
    /// Each names the destination the deployment files it under, so where these screens sit — and
    /// what they are called — is decided once, on the server, for every client. <c>Group</c> is only
    /// the fallback for a client that has not been able to ask.
    /// </remarks>
    public static IReadOnlyList<NavigationItem> Items { get; } = [
        new NavigationItem("Users", "", typeof(UsersPage)) {
            Id = "account.users",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManageUsers,
        },
        new NavigationItem("Role permissions", "", typeof(RolePermissionsPage)) {
            Id = "authz.roles",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManageRoles,
        },
        new NavigationItem("Navigation", "", typeof(NavigationLayoutPage)) {
            Id = "navigation.layout",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManageNavigation,
        },
        new NavigationItem("Plugins", "", typeof(PluginsPage)) {
            Id = "plugins.library",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManagePlugins,
        },
    ];
}
