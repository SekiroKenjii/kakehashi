namespace Kakehashi.Modules.Activity.Application.Activity {
  // Spelled as the server spells them, and deliberately not exhaustive: a kind this client has
  // never heard of still arrives, still counts, and is drawn with its raw value — which is why kind
  // is a string on the wire in the first place.
  public static class ActivityKinds {
    public const string SignedIn = "SignedIn";
    public const string SignedOut = "SignedOut";
    public const string NewDeviceSignedIn = "NewDeviceSignedIn";
    public const string SessionRevoked = "SessionRevoked";
    public const string SessionRevokedByAdmin = "SessionRevokedByAdmin";
    public const string FailedSignIn = "FailedSignIn";
    public const string PasswordChanged = "PasswordChanged";
    public const string AppUpdated = "AppUpdated";
    public const string ThemeChanged = "ThemeChanged";
  }
}
