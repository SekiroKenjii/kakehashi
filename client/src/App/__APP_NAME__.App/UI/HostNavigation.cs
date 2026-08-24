using System;
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
    /// The two screens the shell registers itself, which have no pane item of their own.
    /// </summary>
    /// <remarks>
    /// Home is the one fixed destination and Settings is chrome, so neither is in <see cref="Items"/>
    /// — but both answer to a navigation key, so a plugin may not claim one. The shell and the
    /// plugin loader read this list rather than each spelling the pair.
    /// </remarks>
    public static IReadOnlyList<Type> ShellPages { get; } = [typeof(HomePage), typeof(SettingsPage)];

    /// <summary>
    /// The host's own pane destinations. Home is not here: it is the one fixed destination, and the
    /// shell owns it.
    /// </summary>
    /// <remarks>
    /// The menu items name the destination the deployment files them under, so where those screens
    /// sit — and what they are called — is decided once, on the server, for every client.
    /// <c>Group</c> is only the fallback for a client that has not been able to ask.
    /// <para>
    /// Plugins is the exception, and names no destination: it sits in the footer beside the account
    /// row, which is not a place the deployment's model can express. An item with no id is one the
    /// deployment was never offered, so this client places it and this client gates it.
    /// </para>
    /// <para>
    /// Glyphs are written as escapes, never as the character: a Private Use Area code point shows up
    /// as nothing in most editors and diffs (docs/adr/0013-client-owned-icon-vocabulary.md).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<NavigationItem> Items { get; } = [
        new NavigationItem("Users", "\uE716", typeof(UsersPage)) {
            Id = "account.users",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManageUsers,
        },
        new NavigationItem("Role permissions", "\uE192", typeof(RolePermissionsPage)) {
            Id = "authz.roles",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManageRoles,
        },
        new NavigationItem("Navigation", "\uE816", typeof(NavigationLayoutPage)) {
            Id = "navigation.layout",
            Group = "Administration",
            RequiredPermission = PermissionKeys.ManageNavigation,
        },
        new NavigationItem(
            "Plugins", "\uE74C", typeof(PluginsPage), NavigationItemPlacement.Footer) {
            RequiredPermission = PermissionKeys.ManagePlugins,
        },
    ];
}
