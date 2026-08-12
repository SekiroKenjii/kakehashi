namespace Kakehashi.Modules.Auth.UI {
  // Broadcast on WeakReferenceMessenger.Default whenever the current AuthSession is set or
  // cleared, so auth-aware UI refreshes without polling.
  public sealed class AuthSessionChangedMessage {
  }
}
