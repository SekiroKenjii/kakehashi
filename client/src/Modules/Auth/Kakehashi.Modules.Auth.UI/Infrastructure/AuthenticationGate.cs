using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.RestoreSession;
using Kakehashi.Modules.Auth.UI.Views;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  /// <summary>
  /// The startup login gate. When authentication is configured it first tries a silent session
  /// restore (refresh-token exchange); if that fails it shows the <see cref="LoginWindow"/> and waits
  /// for interactive sign-in. When authentication is not configured it returns immediately.
  /// </summary>
  public sealed class AuthenticationGate : IAuthenticationGate {
    private readonly IServiceProvider _services;
    private readonly AuthOptions _options;

    public AuthenticationGate(IServiceProvider services, IOptions<AuthOptions> options) {
      ArgumentNullException.ThrowIfNull(services);
      ArgumentNullException.ThrowIfNull(options);

      _services = services;
      _options = options.Value;
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken) {
      if (!_options.IsConfigured) {
        return;
      }

      var sender = _services.GetRequiredService<ISender>();
      var restore = await sender.Send(new RestoreSessionCommand(), cancellationToken);
      if (restore.IsSuccess) {
        return;
      }

      await ShowLoginWindowAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task ShowLoginWindowAsync(CancellationToken cancellationToken) {
      var window = _services.GetRequiredService<LoginWindow>();
      window.Activate();

      // The window confirms with the user before closing without a sign-in; a false outcome means
      // the user chose to quit. Surface that as cancellation so startup stops gracefully.
      bool didSignIn = await window.Outcome.WaitAsync(cancellationToken).ConfigureAwait(true);
      if (!didSignIn) {
        throw new OperationCanceledException(
            "Sign-in was cancelled; the application cannot continue.");
      }
    }
  }
}
