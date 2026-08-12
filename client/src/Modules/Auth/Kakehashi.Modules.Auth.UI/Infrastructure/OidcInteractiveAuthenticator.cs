using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel.OidcClient;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernelResult = Kakehashi.SharedKernel.Result;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  // PKCE, state/nonce and id_token validation are OidcClient's; this adapter only maps results to
  // the domain.
  public sealed partial class OidcInteractiveAuthenticator : IInteractiveAuthenticator {
    private readonly AuthOptions _options;
    private readonly SystemBrowser _browser;
    private readonly ILogger<OidcInteractiveAuthenticator> _logger;
    private OidcClient? _client;

    public OidcInteractiveAuthenticator(
        IOptions<AuthOptions> options,
        SystemBrowser browser,
        ILogger<OidcInteractiveAuthenticator> logger) {
      ArgumentNullException.ThrowIfNull(options);
      ArgumentNullException.ThrowIfNull(browser);
      ArgumentNullException.ThrowIfNull(logger);
      _options = options.Value;
      _browser = browser;
      _logger = logger;
    }

    // credentials is ignored: the point of this flow is that the password is typed into the
    // authorization server's page and never reaches this process.
    public async Task<Result<AuthSession>> LoginAsync(
        SignInCredentials? credentials, CancellationToken cancellationToken) {
      if (!_options.IsConfigured) {
        return SharedKernelResult.Failure<AuthSession>(AuthErrors.NotConfigured);
      }

      using var activity = AuthTelemetry.Source.StartActivity("Auth.Login");
      try {
        // BrowserTimeout caps how long the loopback listener waits for the browser callback.
        var login = await GetClient()
            .LoginAsync(new LoginRequest { BrowserTimeout = 120 }, cancellationToken)
            .ConfigureAwait(false);
        if (login.IsError) {
          LogFlowError("login", login.Error);
          // The browser adapter reports failures as BrowserResultType names, hence the strings.
          return SharedKernelResult.Failure<AuthSession>(login.Error switch {
            "UserCancel" => AuthErrors.LoginCancelled,
            "Timeout" => AuthErrors.LoginTimedOut,
            _ => AuthErrors.LoginFailed,
          });
        }

        return AuthSession.Create(
            login.AccessToken,
            login.IdentityToken,
            login.RefreshToken,
            login.AccessTokenExpiration,
            ResolveDisplayName(login.User),
            login.User?.FindFirst("email")?.Value,
            ResolveRoles(login.User?.Claims));
      } catch (Exception ex) {
        LogFlowException("login", ex);
        return SharedKernelResult.Failure<AuthSession>(AuthErrors.LoginFailed);
      }
    }

    public async Task<Result<AuthSession>> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken) {
      if (!_options.IsConfigured) {
        return SharedKernelResult.Failure<AuthSession>(AuthErrors.NotConfigured);
      }

      using var activity = AuthTelemetry.Source.StartActivity("Auth.Refresh");
      try {
        var refresh = await GetClient()
            .RefreshTokenAsync(refreshToken, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (refresh.IsError) {
          LogFlowError("refresh", refresh.Error);
          return SharedKernelResult.Failure<AuthSession>(AuthErrors.RefreshFailed);
        }

        var (displayName, email, roles) =
            await FetchIdentityAsync(refresh.AccessToken, cancellationToken).ConfigureAwait(false);
        return AuthSession.Create(
            refresh.AccessToken,
            refresh.IdentityToken,
            refresh.RefreshToken,
            refresh.AccessTokenExpiration,
            displayName,
            email,
            roles);
      } catch (Exception ex) {
        LogFlowException("refresh", ex);
        return SharedKernelResult.Failure<AuthSession>(AuthErrors.RefreshFailed);
      }
    }

    public async Task LogoutAsync(AuthSession? session, CancellationToken cancellationToken) {
      if (!_options.IsConfigured) {
        _client = null;
        return;
      }

      using var activity = AuthTelemetry.Source.StartActivity("Auth.Logout");
      try {
        // Drives the end-session endpoint in the system browser so the authorization server clears
        // its own session cookie. Without this, only local tokens are dropped and the next sign-in
        // completes silently via the still-valid server SSO session, with no credentials prompt.
        await GetClient()
            .LogoutAsync(new LogoutRequest { IdTokenHint = session?.IdToken }, cancellationToken)
            .ConfigureAwait(false);
      } catch (Exception ex) {
        // Best-effort: the SignOut use case clears the local session and refresh token regardless.
        LogFlowException("logout", ex);
      } finally {
        _client = null;
      }
    }

    private OidcClient GetClient() {
      return _client ??= new OidcClient(new OidcClientOptions {
        Authority = _options.Authority,
        ClientId = _options.ClientId,
        ClientSecret = _options.ClientSecret,
        Scope = _options.Scope,
        RedirectUri = _options.RedirectUri,
        PostLogoutRedirectUri = _options.RedirectUri,
        Browser = _browser,
      });
    }

    // Refresh responses carry no identity claims, so without this a silently restored session
    // would have no display name or email. Best-effort: the session works without identity.
    //
    // Internal because InAppAuthenticator needs the same answer after its own sign-in, and taking
    // it from here means one implementation of discovery, one of the userinfo call, and no second
    // opinion about which claim holds the display name.
    internal async Task<(string? DisplayName, string? Email, IReadOnlyList<string> Roles)>
        FetchIdentityAsync(string accessToken, CancellationToken cancellationToken) {
      try {
        var userInfo = await GetClient()
            .GetUserInfoAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);
        if (userInfo.IsError) {
          LogFlowError("userinfo", userInfo.Error);
          return (null, null, []);
        }

        string? displayName =
            FindClaim(userInfo.Claims, "name") ?? FindClaim(userInfo.Claims, "preferred_username");
        return (displayName, FindClaim(userInfo.Claims, "email"), ResolveRoles(userInfo.Claims));
      } catch (Exception ex) {
        LogFlowException("userinfo", ex);
        return (null, null, []);
      }
    }

    private static string? FindClaim(IEnumerable<Claim> claims, string type) {
      return claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }

    // Both spellings, because there is no standard one: Duende and legacy IdentityServer emit
    // role, Entra and this product's own backend emit roles, and which authorization server sits
    // behind Auth:Authority is a deployment choice. Reading one spelling silently costs the user
    // every permission.
    private static IReadOnlyList<string> ResolveRoles(IEnumerable<Claim>? claims) {
      return claims is null
          ? []
          : [.. claims.Where(claim => claim.Type is "role" or "roles").Select(claim => claim.Value)];
    }

    private static string? ResolveDisplayName(ClaimsPrincipal? user) {
      return user?.FindFirst("name")?.Value
          ?? user?.FindFirst("preferred_username")?.Value
          ?? user?.Identity?.Name;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC {Flow} failed: {Error}")]
    private partial void LogFlowError(string flow, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC {Flow} threw an exception.")]
    private partial void LogFlowException(string flow, Exception exception);
  }
}
