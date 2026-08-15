using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn;

/// <summary>Runs the sign-in flow, then stores the session and persists the refresh token.</summary>
public sealed class SignInCommandHandler : IRequestHandler<SignInCommand, Result>
{
    private readonly IInteractiveAuthenticator _authenticator;
    private readonly IAuthSessionAccessor _session;
    private readonly ITokenStore _tokenStore;

    public SignInCommandHandler(
        IInteractiveAuthenticator authenticator,
        IAuthSessionAccessor session,
        ITokenStore tokenStore)
    {
        _authenticator = authenticator;
        _session = session;
        _tokenStore = tokenStore;
    }

    public async Task<Result> Handle(SignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _authenticator.LoginAsync(request.Credentials, cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        var session = result.Value;
        _session.Set(session);

        if (session.HasRefreshToken)
        {
            await _tokenStore.SaveRefreshTokenAsync(session.RefreshToken!, cancellationToken);
        }

        return Result.Success();
    }
}
