using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut;
using Kakehashi.Modules.Auth.Application.Sessions.Events;
using Kakehashi.Modules.Auth.Domain;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Auth.Application.Tests.Sessions.Commands;

public sealed class SignOutCommandHandlerTests
{
    private readonly IInteractiveAuthenticator _authenticator =
        Substitute.For<IInteractiveAuthenticator>();
    private readonly IAuthSessionAccessor _session = Substitute.For<IAuthSessionAccessor>();
    private readonly ITokenStore _tokenStore = Substitute.For<ITokenStore>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();

    private SignOutCommandHandler CreateHandler()
    {
        return new SignOutCommandHandler(_authenticator, _session, _tokenStore, _publisher);
    }

    [Fact]
    public async Task Handle_SignOut_EndsServerSessionClearsStateAndPublishesNotification()
    {
        var session = AuthSession.Create(
            "access", "id-token", "refresh", DateTimeOffset.UtcNow.AddMinutes(5), "Ada").Value;
        _session.Current.Returns(session);

        var result = await CreateHandler().Handle(new SignOutCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _authenticator.Received(1).LogoutAsync(session, Arg.Any<CancellationToken>());
        _session.Received(1).Clear();
        await _tokenStore.Received(1).ClearAsync(Arg.Any<CancellationToken>());
        await _publisher.Received(1)
            .Publish(Arg.Any<UserSignedOutNotification>(), Arg.Any<CancellationToken>());
    }
}
