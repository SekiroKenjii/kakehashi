namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// The kinds of app-level fact the host notices and announces.
  /// </summary>
  /// <remarks>
  /// An enum rather than a string, because the announcement crosses an assembly boundary: the host
  /// raises it and a feature module reacts to it, and the two must agree on the vocabulary. Two
  /// matching string literals in two assemblies agree until somebody edits one of them; an enum is
  /// checked by the compiler.
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
  /// It exists so the host does not have to know who cares. The activity module wants two of these
  /// forwarded to the server — they are the only facts in the feed that no server can observe for
  /// itself — and the host cannot reference a feature module to hand them over directly.
  /// <para>
  /// Announced rather than returned: a recipient that is not listening yet simply misses it, so
  /// anything that must not be missed has to be announced after the recipients exist. The host defers
  /// the app-update announcement for exactly that reason.
  /// </para>
  /// </remarks>
  public sealed record AppActivityRecordedMessage(AppActivityKind Kind);
}
