using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Application.Abstractions;
using Kakehashi.Application.Abstractions.Security;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Domain;
using Microsoft.Extensions.Options;

namespace Kakehashi.Modules.Auth.UI.Infrastructure;

/// <summary>
/// Holds the current <see cref="AuthSession"/> and serves the access token to the backend client
/// (<see cref="IAccessTokenProvider"/>). When a token is requested close to expiry it refreshes
/// transparently, serialising concurrent refreshes through a semaphore. Registered as a singleton.
/// </summary>
public sealed class AuthSessionAccessor : IAuthSessionAccessor, IAccessTokenProvider
{
    private readonly IInteractiveAuthenticator _authenticator;
    private readonly ITokenStore _tokenStore;
    private readonly IClock _clock;
    private readonly TimeSpan _refreshSkew;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile AuthSession? _current;

    public AuthSessionAccessor(
        IInteractiveAuthenticator authenticator,
        ITokenStore tokenStore,
        IClock clock,
        IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        _authenticator = authenticator;
        _tokenStore = tokenStore;
        _clock = clock;
        _refreshSkew = TimeSpan.FromSeconds(Math.Max(0, options.Value.RefreshSkewSeconds));
    }

    public AuthSession? Current => _current;

    public DateTimeOffset? SignedInAtUtc { get; private set; }

    public void Set(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _current = session;
        SignedInAtUtc = _clock.UtcNow;
        WeakReferenceMessenger.Default.Send(new AuthSessionChangedMessage());
    }

    public void Clear()
    {
        _current = null;
        SignedInAtUtc = null;
        WeakReferenceMessenger.Default.Send(new AuthSessionChangedMessage());
    }

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var session = _current;
        if (session is null)
        {
            return null;
        }
        if (!session.NeedsRefresh(_clock.UtcNow, _refreshSkew))
        {
            return session.AccessToken;
        }
        return await RefreshAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RefreshAsync(AuthSession stale, CancellationToken cancellationToken)
    {
        if (!stale.HasRefreshToken)
        {
            return stale.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = _current;
            if (session is null)
            {
                return null;
            }
            // Another caller may have refreshed while we waited for the lock.
            if (!session.NeedsRefresh(_clock.UtcNow, _refreshSkew))
            {
                return session.AccessToken;
            }

            var result = await _authenticator
                .RefreshAsync(session.RefreshToken!, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                return session.AccessToken; // keep the current token; the call may still succeed or 401
            }

            var refreshed = session.WithRefreshedTokens(
                result.Value.AccessToken,
                result.Value.IdToken,
                result.Value.RefreshToken,
                result.Value.ExpiresAtUtc);
            _current = refreshed;
            if (refreshed.HasRefreshToken)
            {
                await _tokenStore
                    .SaveRefreshTokenAsync(refreshed.RefreshToken!, cancellationToken)
                    .ConfigureAwait(false);
            }
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
