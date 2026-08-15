using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.App.Services;
using Kakehashi.Modules.Auth.UI;

namespace Kakehashi.App.Hosting.Orchestration;

/// <summary>
/// Reads what the signed-in account may do and how this deployment arranges its navigation,
/// between signing in and building the shell.
/// </summary>
/// <remarks>
/// Order 17: after authentication (15), before the shell (20). Re-reads on every session change.
/// docs/adr/0010-startup-orchestrator-ordering.md
/// </remarks>
public sealed class PermissionOrchestrator : IStartupOrchestrator
{
    private readonly PermissionService _permissions;
    private readonly INavigationLayoutService _layout;

    public PermissionOrchestrator(
        PermissionService permissions, INavigationLayoutService layout)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(layout);
        _permissions = permissions;
        _layout = layout;
    }

    public int Order => 17;

    public string Name => nameof(PermissionOrchestrator);

    public string Description => "Checking your access...";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _permissions.RefreshAsync(cancellationToken);
        await _layout.RefreshAsync(cancellationToken);

        // Registered after the first fetch, so startup's own sign-in does not trigger a second call.
        // Named delegate: the messenger's two-generic token overload makes a lambda ambiguous.
        MessageHandler<PermissionOrchestrator, AuthSessionChangedMessage> onSessionChanged =
            static (recipient, message) => {
                // Fire and forget: the messenger's handler is synchronous, and a failed refresh leaves
                // the previous answer standing rather than blocking a sign-in.
                _ = recipient.RefreshAsync();
            };

        WeakReferenceMessenger.Default
            .Register<PermissionOrchestrator, AuthSessionChangedMessage>(this, onSessionChanged);
    }

    /// <summary>Re-reads both answers, in the order the pane needs them.</summary>
    /// <remarks>
    /// The arrangement second, because its <c>Changed</c> event is what rebuilds the pane: doing it
    /// the other way round would rebuild once against the previous account's permissions.
    /// </remarks>
    private async Task RefreshAsync()
    {
        await _permissions.RefreshAsync(CancellationToken.None);
        await _layout.RefreshAsync(CancellationToken.None);
    }
}
