namespace Kakehashi.Modules.Auth.UI {
  // Broadcast via the CommunityToolkit WeakReferenceMessenger whenever the current
  // Domain.AuthSession is set or cleared, so auth-aware UI (the Account page, the
  // shell's account item) can refresh without polling.
  public sealed class AuthSessionChangedMessage {
  }
}
