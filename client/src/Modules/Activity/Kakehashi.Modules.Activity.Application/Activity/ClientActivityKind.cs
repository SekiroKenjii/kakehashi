namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// The facts this client may report about itself.
  /// </summary>
  /// <remarks>
  /// Two, and the shortness of the list is the point rather than a stage it is passing through. The
  /// feed is otherwise written entirely by the server reacting to things it saw for itself, which is
  /// what makes a row in it worth trusting; these two exist because nothing on a server can observe
  /// which build somebody is running or what theme they picked.
  /// <para>
  /// An enum rather than a string, so a caller cannot invent a kind. The server refuses anything
  /// outside its own allow-list regardless — it does not take a client's word for what kind of fact
  /// it is being handed — and this is the same rule stated where the compiler can enforce it.
  /// </para>
  /// <para>
  /// It is the module's own type rather than the host's <c>AppActivityKind</c> because this layer may
  /// not reference the UI contracts, and an architecture test says so. The UI layer maps between them,
  /// which is also where it drops the two host kinds the server already knows about for itself.
  /// </para>
  /// </remarks>
  public enum ClientActivityKind {
    /// <summary>This installation is running a newer build than it was.</summary>
    AppUpdated,

    /// <summary>The chosen theme changed.</summary>
    ThemeChanged,
  }
}
