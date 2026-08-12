namespace Kakehashi.App.Services {
  // The same strings the server's modules declare in their catalogues, with nothing able to check
  // that — the two halves are different languages joined by a wire — so they are collected here
  // rather than typed at each call site.
  //
  // Checking them is presentation. Every route is checked server-side against the same table, so
  // a client that skipped every check here is refused identically.
  public static class PermissionKeys {
    // Guards the Role Permissions screen. Declared by the server's authz module.
    public const string ManageRoles = "roles.manage";

    // Guards the Users screen. Declared by the server's account module.
    public const string ManageUsers = "users.manage";

    // Guards the audit log. Declared by the server's authz module.
    public const string ViewAudit = "audit.view";

    // Guards the Navigation screen. Declared by the server's navigation module.
    //
    // Its own permission rather than roles.manage: arranging a pane and handing out access are
    // different jobs, and somebody trusted to tidy the navigation need not be trusted to grant
    // permissions.
    public const string ManageNavigation = "navigation.manage";
  }
}
