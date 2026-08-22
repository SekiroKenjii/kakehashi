namespace __ROOT_NAMESPACE__.Modules.Activity.Application.Activity;

/// <summary>
/// The entry kinds this client knows by name, as the server spells them.
/// </summary>
/// <remarks>
/// These strings cross the wire and this client switches on them; they must match the server's
/// spelling exactly. The list is not exhaustive: a kind this client has never heard of still
/// arrives, still counts, and is drawn with its raw value.
/// </remarks>
public static class ActivityKinds
{
    public const string SignedIn = "SignedIn";
    public const string SignedOut = "SignedOut";
    public const string NewDeviceSignedIn = "NewDeviceSignedIn";
    public const string SessionRevoked = "SessionRevoked";
    public const string SessionRevokedByAdmin = "SessionRevokedByAdmin";
    public const string FailedSignIn = "FailedSignIn";
    public const string PasswordChanged = "PasswordChanged";
    public const string AppUpdated = "AppUpdated";
    public const string ThemeChanged = "ThemeChanged";
    public const string PluginInstalled = "PluginInstalled";
    public const string PluginSideloaded = "PluginSideloaded";
}
