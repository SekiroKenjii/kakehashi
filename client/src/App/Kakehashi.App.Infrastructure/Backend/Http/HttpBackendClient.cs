using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Infrastructure.Backend.Contracts;

namespace Kakehashi.App.Infrastructure.Backend.Http {
  // Base address and timeout come from IHttpClientFactory (configured in
  // InfrastructureServiceCollectionExtensions), so request URIs here are relative.
  public sealed class HttpBackendClient : IBackendClient {
    private readonly HttpClient _httpClient;

    public HttpBackendClient(HttpClient httpClient) {
      ArgumentNullException.ThrowIfNull(httpClient);
      _httpClient = httpClient;
    }

    public async Task<PingResponse> PingAsync(
        PingRequest request, CancellationToken cancellationToken = default) {
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
  }
}
