namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// The entry kinds this client knows by name, as the server spells them.
  /// </summary>
  /// <remarks>
  /// Named here rather than inline because two things now need them: the row chooses a wording and an
  /// icon per kind, and a summary card states an exact count of one of them. Two files holding the
  /// same literal is how one of them ends up spelled differently.
  /// <para>
  /// The list is not exhaustive and does not have to be. A kind this client has never heard of still
  /// arrives, still counts, and is drawn with its raw value — which is why <c>kind</c> is a string on
  /// the wire in the first place.
  /// </para>
  /// </remarks>
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
