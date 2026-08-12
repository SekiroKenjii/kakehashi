using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RestoreSession {
  public sealed class RestoreSessionCommandHandler : IRequestHandler<RestoreSessionCommand, Result> {
    private readonly IInteractiveAuthenticator _authenticator;
    private readonly IAuthSessionAccessor _session;
    private readonly ITokenStore _tokenStore;

    public RestoreSessionCommandHandler(
        IInteractiveAuthenticator authenticator,
        IAuthSessionAccessor session,
        ITokenStore tokenStore) {
      _authenticator = authenticator;
      _session = session;
      _tokenStore = tokenStore;
    }

    public async Task<Result> Handle(RestoreSessionCommand request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);

      var refreshToken = await _tokenStore.LoadRefreshTokenAsync(cancellationToken);
      if (string.IsNullOrEmpty(refreshToken)) {
        return Result.Failure(AuthErrors.NoStoredSession);
      }

      var result = await _authenticator.RefreshAsync(refreshToken, cancellationToken);
      if (result.IsFailure) {
        await _tokenStore.ClearAsync(cancellationToken);
        return Result.Failure(result.Error);
      }

      var session = result.Value;
      _session.Set(session);
      if (session.HasRefreshToken) {
        await _tokenStore.SaveRefreshTokenAsync(session.RefreshToken!, cancellationToken);
      }

      return Result.Success();
    }
  }
}
