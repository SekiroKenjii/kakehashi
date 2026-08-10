using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.App.Services;
using Kakehashi.Modules.Auth.UI;

namespace Kakehashi.App.Hosting.Orchestration {
  /// <summary>
  /// Reads what the signed-in account may do and how this deployment arranges its navigation,
  /// between signing in and building the shell.
  /// </summary>
  /// <remarks>
  /// Order 17 is not arbitrary: after authentication (15) because both calls need a token, and
  /// before the shell (20) because the shell needs both answers to decide what goes in the
  /// navigation pane. Later would mean the pane is built once wrong and then corrected in front of
  /// the user.
  /// <para>
  /// Two fetches in one orchestrator because they are one moment, not one concern: the permissions
  /// decide which rows are reachable and the arrangement decides where the rows go, and a pane drawn
  /// from one of them is a pane drawn wrong. Splitting them into two ordered steps would buy nothing
  /// but a second progress line saying the same thing.
  /// </para>
  /// <para>
  /// It also re-reads on every session change, so signing in as somebody else replaces their
  /// predecessor's permissions rather than inheriting them — which on a shared machine is the
  /// difference between a lock and a decoration.
  /// </para>
  /// </remarks>
  public sealed class PermissionOrchestrator : IStartupOrchestrator {
    private readonly PermissionService _permissions;
    private readonly INavigationLayoutService _layout;

    public PermissionOrchestrator(
        PermissionService permissions, INavigationLayoutService layout) {
      ArgumentNullException.ThrowIfNull(permissions);
      ArgumentNullException.ThrowIfNull(layout);
      _permissions = permissions;
      _layout = layout;
    }

    public int Order => 17;

    public string Name => nameof(PermissionOrchestrator);

    public string Description => "Checking your access...";

    public async Task ExecuteAsync(CancellationToken cancellationToken) {
      await _permissions.RefreshAsync(cancellationToken);
      await _layout.RefreshAsync(cancellationToken);

      // Registered after the first fetch rather than in the constructor, so the sign-in that
      // startup itself performs does not trigger a second, redundant call. The orchestrator is a
      // singleton, so the weak recipient stays alive for the life of the process.
      // The handler is a named delegate rather than an inline lambda because the messenger has a
      // second two-generic overload taking a token, and a lambda is ambiguous between them.
      MessageHandler<PermissionOrchestrator, AuthSessionChangedMessage> onSessionChanged =
          static (recipient, message) => {
            // Fire and forget: the messenger's handler is synchronous, and a failed refresh leaves
            // the previous answer standing rather than blocking a sign-in. The service swallows
            // and logs its own failures for exactly this reason.
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
    private async Task RefreshAsync() {
      await _permissions.RefreshAsync(CancellationToken.None);
      await _layout.RefreshAsync(CancellationToken.None);
    }
  }
}
