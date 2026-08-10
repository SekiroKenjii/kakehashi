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
  /// <summary>
  /// Drives the OpenID Connect Authorization Code + PKCE flow via <see cref="OidcClient"/> and the
  /// system browser. PKCE, state/nonce and id_token validation are handled by the library; this
  /// adapter only maps results to the domain and never throws for expected failures.
  /// </summary>
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

    /// <summary>
    /// Signs in through the system browser. <paramref name="credentials"/> is ignored: the whole
    /// point of this flow is that the password is typed into the authorization server's page and
    /// never reaches this process.
    /// </summary>
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
          // The browser adapter reports failures as BrowserResultType names.
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
        // Drive the OIDC end-session endpoint in the system browser so the authorization server
        // clears its own session cookie. Without this, only local tokens are dropped and the next
        // sign-in completes silently via the still-valid server SSO session (no credentials prompt).
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

    /// <summary>
    /// Resolves the user's identity from the userinfo endpoint. Refresh responses carry no identity
    /// claims, so without this a silently restored session would have no display name or email.
    /// Best-effort: the session works without identity.
    /// </summary>
    /// <remarks>
    /// Internal because <see cref="InAppAuthenticator"/> needs the same answer after its own
    /// sign-in, and getting it from here means one implementation of discovery, one of the userinfo
    /// call, and no second opinion about which claim holds the display name.
    /// </remarks>
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

    /// <summary>
    /// Collects role claims under both spellings. There is no standard one: Duende and legacy
    /// IdentityServer emit <c>role</c>, Entra and this product's own backend emit <c>roles</c>, and
    /// which authorization server sits behind <c>Auth:Authority</c> is a deployment choice. Reading
    /// both costs a predicate and removes a class of "why is the user missing every permission"
    /// that is invisible from the client side.
    /// </summary>
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
