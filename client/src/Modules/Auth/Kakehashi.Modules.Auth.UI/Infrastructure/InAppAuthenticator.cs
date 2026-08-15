using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernelResult = Kakehashi.SharedKernel.Result;

namespace Kakehashi.Modules.Auth.UI.Infrastructure;

/// <summary>
/// Signs in by posting credentials straight to the authorization server, with no browser and no
/// loopback listener. Registered when <see cref="AuthOptions.Mode"/> is
/// <see cref="AuthMode.InApp"/>.
/// </summary>
/// <remarks>
/// Refreshing delegates to <see cref="OidcInteractiveAuthenticator"/>: the server issues both
/// modes' tokens through one provider and rotates them on one standard endpoint, so a session
/// that began here and one that began in a browser have the same lifecycle from the second
/// request onward.
/// </remarks>
public sealed partial class InAppAuthenticator : IInteractiveAuthenticator, IDisposable
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new();
    private readonly OidcInteractiveAuthenticator _oidc;
    private readonly AuthOptions _options;
    private readonly ILogger<InAppAuthenticator> _logger;

    public InAppAuthenticator(
        OidcInteractiveAuthenticator oidc,
        IOptions<AuthOptions> options,
        ILogger<InAppAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(oidc);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _oidc = oidc;
        _options = options.Value;
        _logger = logger;

        // Unvalidated on purpose: a machine name is user-controlled, and a header the parser
        // dislikes must degrade to a blank device, never to a sign-in that throws before the network.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DeviceLabel());
    }

    /// <summary>
    /// What the server will record as this session's device.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpClient"/> sends no User-Agent by default, and the server reads that header
    /// to fill the device column the Account page shows — without one, every in-app session is a
    /// blank row. The browser flow sends the browser's own.
    /// </remarks>
    public static string DeviceLabel()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        var number = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";

        return $"Kakehashi-Desktop/{number} ({Environment.MachineName})";
    }

    public async Task<Result<AuthSession>> LoginAsync(
        SignInCredentials? credentials, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return SharedKernelResult.Failure<AuthSession>(AuthErrors.NotConfigured);
        }

        if (credentials is null)
        {
            // Nothing to send. This is a wiring mistake, not a user one, so it does not get a
            // user-facing message of its own.
            return SharedKernelResult.Failure<AuthSession>(AuthErrors.LoginFailed);
        }

        using var activity = AuthTelemetry.Source.StartActivity("Auth.Login.InApp");
        try
        {
            using var response = await _http.PostAsJsonAsync(
                Endpoint("account/sign-in"),
                new { email = credentials.Email, password = credentials.Password },
                _json,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogSignInFailed((int)response.StatusCode);

                // The server answers a wrong password and an unknown address identically so the form
                // reveals no addresses. Passing its message through keeps that; inventing one loses it.
                return SharedKernelResult.Failure<AuthSession>(
                    await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var tokens = await response.Content
                .ReadFromJsonAsync<TokenResponse>(_json, cancellationToken).ConfigureAwait(false);

            if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                return SharedKernelResult.Failure<AuthSession>(AuthErrors.LoginFailed);
            }

            var (displayName, email, roles) = await _oidc
                .FetchIdentityAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);

            return AuthSession.Create(
                tokens.AccessToken,
                tokens.IdToken,
                tokens.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn),
                displayName,
                email ?? credentials.Email,
                roles);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            LogSignInException(ex);

            return SharedKernelResult.Failure<AuthSession>(AuthErrors.AccountRequestFailed);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SharedKernelResult.Failure<AuthSession>(AuthErrors.LoginCancelled);
        }
        catch (TaskCanceledException ex)
        {
            LogSignInException(ex);

            return SharedKernelResult.Failure<AuthSession>(AuthErrors.AccountRequestFailed);
        }
    }

    public Task<Result<AuthSession>> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken)
    {
        return _oidc.RefreshAsync(refreshToken, cancellationToken);
    }

    /// <summary>
    /// Ends the session server-side. There is no browser cookie to clear here — that is what
    /// <c>/end_session</c> exists for — so this only needs the session row gone, which is what
    /// stops its refresh token working.
    /// </summary>
    public async Task LogoutAsync(AuthSession? session, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured || session is null)
        {
            return;
        }

        using var activity = AuthTelemetry.Source.StartActivity("Auth.Logout.InApp");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("account/sign-out"));
            // The session's own token, never one from IAccessTokenProvider: that provider refreshes
            // near expiry, so minting one to revoke its own session races itself.
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogSignOutFailed((int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Best-effort, exactly as in the browser flow: the sign-out use case drops the local
            // session and the stored refresh token whether or not the server heard about it.
            LogSignOutException(ex);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private string Endpoint(string path)
    {
        return $"{_options.Authority.TrimEnd('/')}/{path}";
    }

    private static async Task<Error> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content
                .ReadFromJsonAsync<ServerError>(_json, cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(payload?.Message)
                ? AuthErrors.LoginFailed
                : new Error(AuthErrors.LoginFailed.Code, payload.Message);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            return AuthErrors.LoginFailed;
        }
    }

    private sealed record ServerError(string? Error, string? Message);

    /// <summary>The OAuth token response shape the sign-in endpoint deliberately mirrors.</summary>
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);

    [LoggerMessage(Level = LogLevel.Warning, Message = "In-app sign-in failed with status {Status}.")]
    private partial void LogSignInFailed(int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "In-app sign-in threw an exception.")]
    private partial void LogSignInException(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "In-app sign-out failed with status {Status}.")]
    private partial void LogSignOutFailed(int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "In-app sign-out threw an exception.")]
    private partial void LogSignOutException(Exception exception);
}
