using System;
using System.Collections.Generic;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Domain {
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

    public IReadOnlyList<string> Roles { get; }

    public bool HasRefreshToken => !string.IsNullOrEmpty(RefreshToken);

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

    // utcNow is a parameter so the domain keeps no clock dependency.
    public bool NeedsRefresh(DateTimeOffset utcNow, TimeSpan refreshSkew) {
      return utcNow >= ExpiresAtUtc - refreshSkew;
    }

    // Identity is carried over because refresh responses hold no identity claims, and the previous
    // refresh token is kept when the server does not rotate it.
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
