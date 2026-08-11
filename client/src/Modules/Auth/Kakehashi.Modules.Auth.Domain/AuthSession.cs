using System;
using System.Collections.Generic;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Domain {
  // An authenticated session: the tokens obtained from the authorization server plus the minimal
  // identity needed to present the signed-in user. Created through Create, which
  // enforces that a session always carries a non-empty access token.
  public sealed class AuthSession {
    private AuthSession(
        string accessToken,
        string? idToken,
        string? refreshToken,
        DateTimeOffset expiresAtUtc,
        string? displayName,
        string? email,
        IReadOnlyList<string> roles) {
      AccessToken = accessToken;
      IdToken = idToken;
      RefreshToken = refreshToken;
      ExpiresAtUtc = expiresAtUtc;
      DisplayName = displayName;
      Email = email;
      Roles = roles;
    }

    public string AccessToken { get; }

    public string? IdToken { get; }

    public string? RefreshToken { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public string? DisplayName { get; }

    public string? Email { get; }

    // The user's role memberships, as asserted by the authorization server.
    public IReadOnlyList<string> Roles { get; }

    public bool HasRefreshToken => !string.IsNullOrEmpty(RefreshToken);

    // Creates a session, requiring a non-empty access token.
    public static Result<AuthSession> Create(
        string accessToken,
        string? idToken,
        string? refreshToken,
        DateTimeOffset expiresAtUtc,
        string? displayName,
        string? email = null,
        IReadOnlyList<string>? roles = null) {
      if (string.IsNullOrWhiteSpace(accessToken)) {
        return Result.Failure<AuthSession>(AuthErrors.AccessTokenRequired);
      }
      return new AuthSession(
          accessToken, idToken, refreshToken, expiresAtUtc, displayName, email, roles ?? []);
    }

    // Whether the access token is expired or within refreshSkew of expiry. The
    // current time is passed in so the domain keeps no clock dependency.
    public bool NeedsRefresh(DateTimeOffset utcNow, TimeSpan refreshSkew) {
      return utcNow >= ExpiresAtUtc - refreshSkew;
    }

    // Returns a copy with refreshed tokens, preserving the identity (refresh responses carry no
    // identity claims) and the previous refresh token when the server does not rotate it.
    public AuthSession WithRefreshedTokens(
        string accessToken, string? idToken, string? refreshToken, DateTimeOffset expiresAtUtc) {
      return new AuthSession(
          accessToken,
          idToken ?? IdToken,
          string.IsNullOrEmpty(refreshToken) ? RefreshToken : refreshToken,
          expiresAtUtc,
          DisplayName,
          Email,
          Roles);
    }
  }
}
