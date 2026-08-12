namespace Kakehashi.UI.Contracts {
  // An enum rather than a string because the announcement crosses an assembly boundary: the host
  // raises it and a feature module reacts to it. Two matching string literals in two assemblies
  // agree until somebody edits one of them; an enum is checked by the compiler.
  public enum AppActivityKind {
    SignedIn,

    SignedOut,

    // This installation is now running a newer build than it was.
    AppUpdated,

    ThemeChanged,
  }

  // Exists so the host does not have to know who cares: the activity module forwards two of these
  // to the server - the only facts in the feed no server can observe for itself - and the host
  // cannot reference a feature module to hand them over directly.
  //
  // Announced rather than returned, so a recipient that is not listening yet simply misses it.
  // Anything that must not be missed has to be announced after the recipients exist; the host
  // defers the app-update announcement for exactly that reason.
  public sealed record AppActivityRecordedMessage(AppActivityKind Kind);
}
