using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions.Events;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Logging;

namespace Kakehashi.Modules.Auth.UI.Infrastructure;

/// <summary>
/// Reacts to <see cref="UserSignedOutNotification"/> by forcing the modal re-sign-in flow, so
/// every sign-out path (account flyout, account page, future callers) behaves the same. The flow
/// is enqueued to the UI thread and not awaited: blocking the publisher would keep the SignOut
/// use case running until the user completes the next sign-in.
/// </summary>
public sealed partial class SignedOutReauthenticationHandler
    : INotificationHandler<UserSignedOutNotification>
{
    private readonly ReauthenticationService _reauthentication;
    private readonly IMainWindowProvider _mainWindowProvider;
    private readonly ILogger<SignedOutReauthenticationHandler> _logger;

    public SignedOutReauthenticationHandler(
        ReauthenticationService reauthentication,
        IMainWindowProvider mainWindowProvider,
        ILogger<SignedOutReauthenticationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(reauthentication);
        ArgumentNullException.ThrowIfNull(mainWindowProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _reauthentication = reauthentication;
        _mainWindowProvider = mainWindowProvider;
        _logger = logger;
    }

    public Task Handle(UserSignedOutNotification notification, CancellationToken cancellationToken)
    {
        _mainWindowProvider.MainWindow?.DispatcherQueue.TryEnqueue(async () => {
            try
            {
                await _reauthentication.RequireSignInAsync();
            }
            catch (Exception ex)
            {
                LogReauthenticationFailed(ex);
            }
        });
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Forced re-sign-in after sign-out failed.")]
    private partial void LogReauthenticationFailed(Exception exception);
}
