namespace Kakehashi.Modules.Auth.UI;

/// <summary>
/// Broadcast via the CommunityToolkit <c>WeakReferenceMessenger</c> whenever the current
/// <see cref="Domain.AuthSession"/> is set or cleared, so auth-aware UI (the Account page, the
/// shell's account item) can refresh without polling.
/// </summary>
public sealed class AuthSessionChangedMessage
{
}
