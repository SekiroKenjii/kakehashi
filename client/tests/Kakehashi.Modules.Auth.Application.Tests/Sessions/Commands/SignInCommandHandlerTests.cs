using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Auth.Application.Tests.Sessions.Commands;

public sealed class SignInCommandHandlerTests
{
    private readonly IInteractiveAuthenticator _authenticator =
        Substitute.For<IInteractiveAuthenticator>();
    private readonly IAuthSessionAccessor _session = Substitute.For<IAuthSessionAccessor>();
    private readonly ITokenStore _tokenStore = Substitute.For<ITokenStore>();

    private SignInCommandHandler CreateHandler()
    {
        return new SignInCommandHandler(_authenticator, _session, _tokenStore);
    }

    private static AuthSession Session(string? refreshToken)
    {
        return AuthSession.Create(
            "access", "id", refreshToken, DateTimeOffset.UtcNow.AddMinutes(5), "Ada").Value;
    }

    [Fact]
    public async Task Handle_SuccessfulLogin_SetsSessionAndSavesRefreshToken()
    {
        _authenticator.LoginAsync(Arg.Any<SignInCredentials?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Session("refresh")));

        var result = await CreateHandler().Handle(new SignInCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _session.Received(1).Set(Arg.Is<AuthSession>(s => s != null && s.AccessToken == "access"));
        await _tokenStore.Received(1).SaveRefreshTokenAsync("refresh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailedLogin_ReturnsFailureAndDoesNotPersist()
    {
        _authenticator.LoginAsync(Arg.Any<SignInCredentials?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AuthSession>(AuthErrors.LoginFailed));

        var result = await CreateHandler().Handle(new SignInCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.LoginFailed, result.Error);
        _session.DidNotReceive().Set(Arg.Any<AuthSession>());
        await _tokenStore.DidNotReceive()
            .SaveRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LoginWithoutRefreshToken_DoesNotSaveToken()
    {
        _authenticator.LoginAsync(Arg.Any<SignInCredentials?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Session(refreshToken: null)));

        var result = await CreateHandler().Handle(new SignInCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _session.Received(1).Set(Arg.Any<AuthSession>());
        await _tokenStore.DidNotReceive()
            .SaveRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
