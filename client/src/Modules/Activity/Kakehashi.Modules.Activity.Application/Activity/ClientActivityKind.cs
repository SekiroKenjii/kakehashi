namespace Kakehashi.Modules.Activity.Application.Activity {
  // Two, and the shortness of the list is the point rather than a stage it is passing through. The
  // feed is otherwise written entirely by the server reacting to things it saw for itself, which is
  // what makes a row in it worth trusting; these two exist because nothing on a server can observe
  // which build somebody is running or what theme they picked.
  //
  // An enum rather than a string, so a caller cannot invent a kind. The server refuses anything
  // outside its own allow-list regardless, and this states the same rule where the compiler can
  // enforce it.
  //
  // The module's own type rather than the host's AppActivityKind because this layer may not
  // reference the UI contracts, and an architecture test says so. The UI layer maps between them,
  // which is also where it drops the two host kinds the server already knows about for itself.
  public enum ClientActivityKind {
    AppUpdated,

    ThemeChanged,
  }
}
