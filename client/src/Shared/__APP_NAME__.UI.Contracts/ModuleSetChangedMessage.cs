namespace __ROOT_NAMESPACE__.UI.Contracts;

/// <summary>
/// Broadcast via the CommunityToolkit <c>WeakReferenceMessenger</c> whenever a module is attached
/// or detached, so composition-aware UI (the shell's nav rail, the home page) can rebuild without
/// polling.
/// </summary>
public sealed class ModuleSetChangedMessage
{
}
