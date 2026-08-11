using System.Collections.Generic;
using Kakehashi.App.Services;
using Kakehashi.UI.Contracts;

namespace Kakehashi.App.UI {
  // The destinations the host owns, in the same shape a module contributes.
  //
  // The administration screens are not a feature module — they govern every module, and a module
  // that governed the others would reach across the boundary the architecture tests hold. But
  // they are still pane items, and hard-coding them into the shell's XAML meant they could not be
  // grouped, ordered or gated by the same code that handles everyone else's. Declaring them here
  // gives the shell exactly one list to render.
  public static class HostNavigation {
    // Home is not here: it is the one fixed destination, and the shell owns it.
    //
    // Each names the destination the deployment files it under, so where these screens sit — and
    // what they are called — is decided once, on the server, for every client. Group is only
    // the fallback for a client that has not been able to ask.
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
    ];
  }
}
