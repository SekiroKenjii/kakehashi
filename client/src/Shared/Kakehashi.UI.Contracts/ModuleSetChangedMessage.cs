namespace Kakehashi.UI.Contracts {
  // Broadcast via the CommunityToolkit WeakReferenceMessenger whenever a module is attached or
  // detached, so composition-aware UI rebuilds without polling.
  public sealed class ModuleSetChangedMessage {
  }
}
