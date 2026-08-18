using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.App.Infrastructure.Backend.Contracts;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Backend.Http;

/// <summary>
/// REST/JSON implementation of <see cref="IBackendClient"/>. The <see cref="HttpClient"/> is supplied
/// by <c>IHttpClientFactory</c> (base address and timeout configured in
/// <c>InfrastructureServiceCollectionExtensions</c>); outbound calls are traced automatically by the
/// OpenTelemetry HTTP-client instrumentation.
/// </summary>
public sealed class HttpBackendClient : IBackendClient
{
    private readonly HttpClient _httpClient;

    public HttpBackendClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<PingResponse> PingAsync(
        PingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response =
            await _httpClient.PostAsJsonAsync("ping", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<PingResponse>(cancellationToken)
            .ConfigureAwait(false);

        return result
            ?? throw new InvalidOperationException("The backend returned an empty ping response.");
    }

    public async Task<SystemResponse> SystemAsync(
        SystemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response =
            await _httpClient.PostAsJsonAsync("system", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<SystemResponse>(cancellationToken)
            .ConfigureAwait(false);

        return result
            ?? throw new InvalidOperationException("The backend returned an empty system response.");
    }
}
