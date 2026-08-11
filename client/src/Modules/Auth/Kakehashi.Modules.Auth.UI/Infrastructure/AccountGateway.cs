using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Security;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Account;
using Kakehashi.Modules.Auth.Application.Sessions;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  // Calls the authorization server's account endpoints (sessions and security activity) with the
  // current access token. Expected failures (offline, expired token, revoked session) surface as
  // Result failures rather than exceptions. Registered as a singleton.
  public sealed partial class AccountGateway : IAccountGateway, IDisposable {
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new();
    private readonly IAccessTokenProvider _tokens;
    private readonly AuthOptions _options;
    private readonly ILogger<AccountGateway> _logger;

    public AccountGateway(
        IAccessTokenProvider tokens,
        IOptions<AuthOptions> options,
        ILogger<AccountGateway> logger) {
      ArgumentNullException.ThrowIfNull(tokens);
      ArgumentNullException.ThrowIfNull(options);
      ArgumentNullException.ThrowIfNull(logger);
      _tokens = tokens;
      _options = options.Value;
      _logger = logger;
    }

    public async Task<Result<IReadOnlyList<RemoteSessionDto>>> GetSessionsAsync(
        CancellationToken cancellationToken) {
      return await GetAsync<IReadOnlyList<RemoteSessionDto>>("account/sessions", cancellationToken)
          .ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<SecurityEventDto>>> GetSecurityActivityAsync(
        int take, CancellationToken cancellationToken) {
      return await GetAsync<IReadOnlyList<SecurityEventDto>>(
          $"account/security-events?take={take}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> RevokeSessionAsync(
        string sessionId, CancellationToken cancellationToken) {
      return await SendAsync(
          HttpMethod.Delete, $"account/sessions/{Uri.EscapeDataString(sessionId)}",
          cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> RevokeAllSessionsAsync(CancellationToken cancellationToken) {
      return await SendAsync(HttpMethod.Post, "account/sessions/revoke-all", cancellationToken)
          .ConfigureAwait(false);
    }

    public async Task<Result<RemoteProfileDto>> GetProfileAsync(CancellationToken cancellationToken) {
      return await GetAsync<RemoteProfileDto>("account/profile", cancellationToken)
          .ConfigureAwait(false);
    }

    public async Task<Result> UpdateProfileAsync(
        string? displayName, string? phone, CancellationToken cancellationToken) {
      return await SendAsync(
          HttpMethod.Put, "account/profile", new { displayName, phone }, cancellationToken)
          .ConfigureAwait(false);
    }

    public async Task<Result> ChangePasswordAsync(
        string currentPassword, string newPassword, CancellationToken cancellationToken) {
      return await SendAsync(
          HttpMethod.Post, "account/password", new { currentPassword, newPassword },
          cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() {
      _http.Dispose();
    }

    private async Task<Result<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class {
      var request = await CreateRequestAsync(HttpMethod.Get, path, cancellationToken)
          .ConfigureAwait(false);
      if (request is null) {
        return Result.Failure<T>(AuthErrors.NotSignedIn);
      }
      try {
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
          LogRequestFailed(path, (int)response.StatusCode);
          return Result.Failure<T>(AuthErrors.AccountRequestFailed);
        }
        var payload = await response.Content
            .ReadFromJsonAsync<T>(_json, cancellationToken).ConfigureAwait(false);
        return payload is null
            ? Result.Failure<T>(AuthErrors.AccountRequestFailed)
            : Result.Success(payload);
      } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
        LogRequestException(path, ex);
        return Result.Failure<T>(AuthErrors.AccountRequestFailed);
      }
    }

    private async Task<Result> SendAsync(
        HttpMethod method, string path, CancellationToken cancellationToken) {
      return await SendAsync(method, path, body: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken cancellationToken) {
      var request = await CreateRequestAsync(method, path, cancellationToken).ConfigureAwait(false);
      if (request is null) {
        return Result.Failure(AuthErrors.NotSignedIn);
      }
      if (body is not null) {
        request.Content = JsonContent.Create(body, options: _json);
      }
      try {
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
          LogRequestFailed(path, (int)response.StatusCode);
          return Result.Failure(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
        }
        return Result.Success();
      } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
        LogRequestException(path, ex);
        return Result.Failure(AuthErrors.AccountRequestFailed);
      }
    }

    // Surfaces the server's validation message (e.g. password policy) when present.
    private static async Task<Error> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken) {
      try {
        var payload = await response.Content
            .ReadFromJsonAsync<ServerError>(_json, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(payload?.Message)
            ? AuthErrors.AccountRequestFailed
            : new Error(payload.Error ?? AuthErrors.AccountRequestFailed.Code, payload.Message);
      } catch (JsonException) {
        return AuthErrors.AccountRequestFailed;
      }
    }

    private sealed record ServerError(string? Error, string? Message);

    private async Task<HttpRequestMessage?> CreateRequestAsync(
        HttpMethod method, string path, CancellationToken cancellationToken) {
      if (!_options.IsConfigured) {
        return null;
      }
      var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
      if (string.IsNullOrEmpty(token)) {
        return null;
      }
      var request = new HttpRequestMessage(
          method, $"{_options.Authority.TrimEnd('/')}/{path}");
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
      return request;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account request {Path} failed with status {Status}.")]
    private partial void LogRequestFailed(string path, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account request {Path} threw an exception.")]
    private partial void LogRequestException(string path, Exception exception);
  }
}
