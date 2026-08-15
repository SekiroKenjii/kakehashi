using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using __ROOT_NAMESPACE__.Modules.Auth.Domain;
using NSubstitute;
using Xunit;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Tests.Sessions.Queries;

public sealed class GetCurrentSessionQueryHandlerTests
{
    private readonly IAuthSessionAccessor _session = Substitute.For<IAuthSessionAccessor>();

    private GetCurrentSessionQueryHandler CreateHandler()
    {
        return new GetCurrentSessionQueryHandler(_session);
    }

    [Fact]
    public async Task Handle_NoSession_ReturnsUnauthenticated()
    {
        _session.Current.Returns((AuthSession?)null);

        var dto = await CreateHandler().Handle(new GetCurrentSessionQuery(), CancellationToken.None);

        Assert.False(dto.IsAuthenticated);
        Assert.Null(dto.DisplayName);
        Assert.Null(dto.ExpiresAtUtc);
    }

    [Fact]
    public async Task Handle_WithSession_ReturnsAuthenticatedWithDisplayName()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        _session.Current.Returns(AuthSession.Create("access", "id", "refresh", expiry, "Ada").Value);

        var dto = await CreateHandler().Handle(new GetCurrentSessionQuery(), CancellationToken.None);

        Assert.True(dto.IsAuthenticated);
        Assert.Equal("Ada", dto.DisplayName);
        Assert.Equal(expiry, dto.ExpiresAtUtc);
    }
}
