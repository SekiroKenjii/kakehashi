using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.UI.Contracts.Services;

namespace Kakehashi.App.Hosting.Orchestration {
  /// <summary>
  /// Runs any registered <see cref="IAuthenticationGate"/> before the shell is created, so a module
  /// (for example Auth) can require sign-in. When no gate is registered this is a no-op and startup
  /// proceeds, which is what keeps the Auth module fully optional.
  /// </summary>
  public sealed class AuthenticationOrchestrator : IStartupOrchestrator {
    private readonly IEnumerable<IAuthenticationGate> _gates;

    public AuthenticationOrchestrator(IEnumerable<IAuthenticationGate> gates) {
      ArgumentNullException.ThrowIfNull(gates);
      _gates = gates;
    }

    public int Order => 15;

    public string Name => nameof(AuthenticationOrchestrator);

    public string Description => "Signing in...";

    public async Task ExecuteAsync(CancellationToken cancellationToken) {
      foreach (var gate in _gates) {
        cancellationToken.ThrowIfCancellationRequested();
        await gate.EnsureAuthenticatedAsync(cancellationToken);
      }
    }
  }
}
