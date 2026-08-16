using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RestoreSession;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.SignIn;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.SignOut;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Events;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using __ROOT_NAMESPACE__.Modules.Auth.Domain;
using __ROOT_NAMESPACE__.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace __ROOT_NAMESPACE__.Modules.Auth.IntegrationTests;

/// <summary>
/// Exercises the Auth module the way the host does: the real mediator pipeline and handlers, wired
/// over in-memory test doubles for the OIDC authenticator, token store and session accessor.
/// </summary>
public sealed class AuthModuleIntegrationTests
{
    private static ServiceProvider BuildProvider(
        IInteractiveAuthenticator authenticator, ITokenStore tokenStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthApplication();
        services.AddSingleton(authenticator);
        services.AddSingleton(tokenStore);
        services.AddSingleton<IAuthSessionAccessor, InMemoryAuthSessionAccessor>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SignIn_ThenGetCurrentSession_AuthenticatesAndStoresRefreshToken()
    {
        var store = new InMemoryTokenStore();
        using var provider = BuildProvider(FakeAuthenticator.Succeeding("Ada", "refresh-1"), store);
        var mediator = provider.GetRequiredService<IMediator>();

        var signIn = await mediator.Send(new SignInCommand());
        Assert.True(signIn.IsSuccess);

        var session = await mediator.Send(new GetCurrentSessionQuery());
        Assert.True(session.IsAuthenticated);
        Assert.Equal("Ada", session.DisplayName);
        Assert.Equal("refresh-1", await store.LoadRefreshTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestoreSession_WithStoredRefreshToken_RefreshesAndAuthenticates()
    {
        var store = new InMemoryTokenStore("stored-refresh");
        using var provider =
            BuildProvider(FakeAuthenticator.Succeeding("Ada", "rotated-refresh"), store);
        var mediator = provider.GetRequiredService<IMediator>();

        var restore = await mediator.Send(new RestoreSessionCommand());

        Assert.True(restore.IsSuccess);
        var session = await mediator.Send(new GetCurrentSessionQuery());
        Assert.True(session.IsAuthenticated);
        Assert.Equal("rotated-refresh", await store.LoadRefreshTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestoreSession_WithNoStoredToken_FailsAndStaysUnauthenticated()
    {
        var store = new InMemoryTokenStore();
        using var provider = BuildProvider(FakeAuthenticator.Succeeding("Ada", "unused"), store);
        var mediator = provider.GetRequiredService<IMediator>();

        var restore = await mediator.Send(new RestoreSessionCommand());

        Assert.True(restore.IsFailure);
        var session = await mediator.Send(new GetCurrentSessionQuery());
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task SignOut_ClearsSessionStoreAndPublishesNotification()
    {
        var store = new InMemoryTokenStore();
        var spy = new SpySignedOutHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthApplication();
        services.AddSingleton<IInteractiveAuthenticator>(FakeAuthenticator.Succeeding("Ada", "refresh-1"));
        services.AddSingleton<ITokenStore>(store);
        services.AddSingleton<IAuthSessionAccessor, InMemoryAuthSessionAccessor>();
        services.AddSingleton<INotificationHandler<UserSignedOutNotification>>(spy);
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        await mediator.Send(new SignInCommand());

        var signOut = await mediator.Send(new SignOutCommand());

        Assert.True(signOut.IsSuccess);
        Assert.Equal(1, spy.HandledCount);
        Assert.Null(await store.LoadRefreshTokenAsync(CancellationToken.None));
        var session = await mediator.Send(new GetCurrentSessionQuery());
        Assert.False(session.IsAuthenticated);
    }

    private sealed class FakeAuthenticator : IInteractiveAuthenticator
    {
        private readonly AuthSession _session;

        private FakeAuthenticator(AuthSession session)
        {
            _session = session;
        }

        public static FakeAuthenticator Succeeding(string displayName, string refreshToken)
        {
            var session = AuthSession.Create(
                "access-token", "id-token", refreshToken, DateTimeOffset.UtcNow.AddMinutes(5), displayName).Value;

            return new FakeAuthenticator(session);
        }

        public Task<Result<AuthSession>> LoginAsync(
            SignInCredentials? credentials, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(_session));
        }

        public Task<Result<AuthSession>> RefreshAsync(
            string refreshToken, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(_session));
        }

        public Task LogoutAsync(AuthSession? session, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTokenStore : ITokenStore
    {
        private string? _refreshToken;

        public InMemoryTokenStore()
        {
        }

        public InMemoryTokenStore(string? seed)
        {
            _refreshToken = seed;
        }

        public Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_refreshToken);
        }

        public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            _refreshToken = refreshToken;

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _refreshToken = null;

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuthSessionAccessor : IAuthSessionAccessor
    {
        public AuthSession? Current { get; private set; }

        public DateTimeOffset? SignedInAtUtc { get; private set; }

        public void Set(AuthSession session)
        {
            Current = session;
            SignedInAtUtc = DateTimeOffset.UtcNow;
        }

        public void Clear()
        {
            Current = null;
            SignedInAtUtc = null;
        }
    }

    private sealed class SpySignedOutHandler : INotificationHandler<UserSignedOutNotification>
    {
        public int HandledCount { get; private set; }

        public Task Handle(UserSignedOutNotification notification, CancellationToken cancellationToken)
        {
            HandledCount++;

            return Task.CompletedTask;
        }
    }
}
