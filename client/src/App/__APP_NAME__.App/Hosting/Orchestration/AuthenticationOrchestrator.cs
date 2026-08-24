using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.UI.Contracts.Services;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>
/// Runs any registered <see cref="IAuthenticationGate"/> before the shell is created, so a module
/// (for example Auth) can require sign-in. When no gate is registered this is a no-op and startup
/// proceeds, which is what keeps the Auth module fully optional.
/// </summary>
public sealed class AuthenticationOrchestrator : IStartupOrchestrator
{
    private readonly IEnumerable<IAuthenticationGate> _gates;

    public AuthenticationOrchestrator(IEnumerable<IAuthenticationGate> gates)
    {
        ArgumentNullException.ThrowIfNull(gates);
        _gates = gates;
    }

    public int Order => StartupOrder.Authentication;

    public string Name => nameof(AuthenticationOrchestrator);

    public string Description => "Signing in...";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        foreach (var gate in _gates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await gate.EnsureAuthenticatedAsync(cancellationToken);
        }
    }
}
