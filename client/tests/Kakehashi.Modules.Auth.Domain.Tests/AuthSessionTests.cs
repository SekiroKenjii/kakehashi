using System;
using Xunit;

namespace Kakehashi.Modules.Auth.Domain.Tests;

public sealed class AuthSessionTests
{
    private static readonly DateTimeOffset _expiry = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithEmptyAccessToken_ReturnsFailure()
    {
        var result =
            AuthSession.Create("   ", idToken: null, refreshToken: null, _expiry, displayName: null);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.AccessTokenRequired, result.Error);
    }

    [Fact]
    public void Create_WithAccessToken_ReturnsSuccess()
    {
        var result = AuthSession.Create("access", "id", "refresh", _expiry, "Ada");

        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value.AccessToken);
        Assert.Equal("id", result.Value.IdToken);
        Assert.Equal("refresh", result.Value.RefreshToken);
        Assert.True(result.Value.HasRefreshToken);
        Assert.Equal("Ada", result.Value.DisplayName);
    }

    [Theory]
    [InlineData(-1, true)]   // one second past expiry
    [InlineData(30, true)]   // inside the 60s refresh skew
    [InlineData(120, false)] // comfortably valid
    public void NeedsRefresh_RespectsSkew(int secondsUntilExpiry, bool expected)
    {
        var session = AuthSession.Create("access", null, null, _expiry, null).Value;
        var now = _expiry.AddSeconds(-secondsUntilExpiry);

        Assert.Equal(expected, session.NeedsRefresh(now, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void WithRefreshedTokens_PreservesIdentity_AndKeepsRefreshTokenWhenNotRotated()
    {
        var original = AuthSession.Create("a1", "id1", "r1", _expiry, "Ada").Value;
        var newExpiry = _expiry.AddHours(1);

        var refreshed =
            original.WithRefreshedTokens("a2", idToken: null, refreshToken: null, newExpiry);

        Assert.Equal("a2", refreshed.AccessToken);
        Assert.Equal("id1", refreshed.IdToken);
        Assert.Equal("r1", refreshed.RefreshToken);
        Assert.Equal("Ada", refreshed.DisplayName);
        Assert.Equal(newExpiry, refreshed.ExpiresAtUtc);
    }

    [Fact]
    public void WithRefreshedTokens_UsesRotatedTokens_WhenProvided()
    {
        var original = AuthSession.Create("a1", "id1", "r1", _expiry, "Ada").Value;

        var refreshed = original.WithRefreshedTokens("a2", "id2", "r2", _expiry);

        Assert.Equal("id2", refreshed.IdToken);
        Assert.Equal("r2", refreshed.RefreshToken);
    }
}
