namespace Kakehashi.App.Services;

/// <summary>
/// The permission keys this client checks, spelled once.
/// </summary>
/// <remarks>
/// These are the same strings the server's modules declare in their catalogues. There is no way
/// to make a compiler check that — the two halves are different languages joined by a wire — so
/// they are collected here rather than typed at each call site, which at least reduces it to one
/// place to get wrong.
/// <para>
/// Checking them is presentation. Every route is checked server-side against the same table, so
/// a client that skipped every check here is refused identically.
/// </para>
/// </remarks>
public static class PermissionKeys
{
    /// <summary>Guards the Role Permissions screen. Declared by the server's authz module.</summary>
    public const string ManageRoles = "roles.manage";

    /// <summary>Guards the Users screen. Declared by the server's account module.</summary>
    public const string ManageUsers = "users.manage";

    /// <summary>Guards the audit log. Declared by the server's authz module.</summary>
    public const string ViewAudit = "audit.view";

    /// <summary>
    /// Guards the Navigation screen. Declared by the server's navigation module.
    /// </summary>
    /// <remarks>
    /// Its own permission rather than roles.manage: arranging a pane and handing out access are
    /// different jobs, and somebody trusted to tidy the navigation need not be trusted to grant
    /// permissions.
    /// </remarks>
    public const string ManageNavigation = "navigation.manage";
}
