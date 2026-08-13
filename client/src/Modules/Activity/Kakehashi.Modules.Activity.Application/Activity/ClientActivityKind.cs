namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// The facts this client may report about itself.
  /// </summary>
  /// <remarks>
  /// Only these two: the server writes the rest of the feed from what it observes itself, refuses
  /// any kind outside its own allow-list, and cannot observe which build is running or which theme
  /// is set. See docs/ACTIVITY.md. An enum rather than a string so a caller cannot invent a kind.
  /// Distinct from the host's <c>AppActivityKind</c> because this layer may not reference the UI
  /// contracts (enforced by an architecture test); the UI layer maps between them.
  /// </remarks>
  public enum ClientActivityKind {
    /// <summary>This installation is running a newer build than it was.</summary>
    AppUpdated,

    ThemeChanged,
  }
}
