using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.RestoreSession;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Auth.Application.Tests.Sessions.Commands {
  public sealed class RestoreSessionCommandHandlerTests {
    private readonly IInteractiveAuthenticator _authenticator =
        Substitute.For<IInteractiveAuthenticator>();
    private readonly IAuthSessionAccessor _session = Substitute.For<IAuthSessionAccessor>();
    private readonly ITokenStore _tokenStore = Substitute.For<ITokenStore>();

    private RestoreSessionCommandHandler CreateHandler() {
      return new RestoreSessionCommandHandler(_authenticator, _session, _tokenStore);
    }

    [Fact]
    public async Task Handle_NoStoredToken_ReturnsFailureWithoutCallingAuthenticator() {
      _tokenStore.LoadRefreshTokenAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

      var result = await CreateHandler().Handle(new RestoreSessionCommand(), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(AuthErrors.NoStoredSession, result.Error);
      await _authenticator.DidNotReceive()
          .RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
      _session.DidNotReceive().Set(Arg.Any<AuthSession>());
    }

    [Fact]
    public async Task Handle_RefreshSucceeds_SetsSessionAndSavesRotatedToken() {
      _tokenStore.LoadRefreshTokenAsync(Arg.Any<CancellationToken>()).Returns("old-refresh");
      var refreshed = AuthSession.Create(
          "access", "id", "new-refresh", DateTimeOffset.UtcNow.AddMinutes(5), "Ada").Value;
      _authenticator.RefreshAsync("old-refresh", Arg.Any<CancellationToken>())
          .Returns(Result.Success(refreshed));

      var result = await CreateHandler().Handle(new RestoreSessionCommand(), CancellationToken.None);

      Assert.True(result.IsSuccess);
      _session.Received(1).Set(refreshed);
      await _tokenStore.Received(1).SaveRefreshTokenAsync("new-refresh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RefreshFails_ClearsStoreAndReturnsFailure() {
      _tokenStore.LoadRefreshTokenAsync(Arg.Any<CancellationToken>()).Returns("old-refresh");
      _authenticator.RefreshAsync("old-refresh", Arg.Any<CancellationToken>())
          .Returns(Result.Failure<AuthSession>(AuthErrors.RefreshFailed));

      var result = await CreateHandler().Handle(new RestoreSessionCommand(), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(AuthErrors.RefreshFailed, result.Error);
      await _tokenStore.Received(1).ClearAsync(Arg.Any<CancellationToken>());
      _session.DidNotReceive().Set(Arg.Any<AuthSession>());
    }
  }
}
