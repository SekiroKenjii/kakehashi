namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// The kinds of app-level fact the host notices and announces.
  /// </summary>
  /// <remarks>
  /// The host raises these and feature modules switch on them across an assembly boundary; an enum
  /// keeps that shared vocabulary compiler-checked, where matching string literals would drift.
  /// </remarks>
  public enum AppActivityKind {
    /// <summary>A session began on this device.</summary>
    SignedIn,

    /// <summary>A session ended on this device.</summary>
    SignedOut,

    /// <summary>This installation is running a newer build than it was.</summary>
    AppUpdated,

    /// <summary>The chosen theme changed.</summary>
    ThemeChanged,
  }

  /// <summary>
  /// Raised when the host records an app-level fact about this device.
  /// </summary>
  /// <remarks>
  /// The host cannot reference a feature module, so it broadcasts instead of calling; the activity
  /// module forwards two of these kinds to the server — the only feed facts no server can observe
  /// for itself. A message published before a recipient registers is silently missed, so anything
  /// that must not be missed is announced only after the recipients exist (the host defers the
  /// app-update announcement for this reason).
  /// </remarks>
  public sealed record AppActivityRecordedMessage(AppActivityKind Kind);
}
