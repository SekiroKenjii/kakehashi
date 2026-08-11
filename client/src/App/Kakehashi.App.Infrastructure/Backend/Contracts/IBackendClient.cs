using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  // Transport-agnostic gateway to the separate backend service. Callers depend on this interface and
  // never on a concrete transport; the registered implementation (HTTP or gRPC) is chosen from
  // BackendOptions.Protocol. Add backend operations here and implement them in both
  // HttpBackendClient and GrpcBackendClient.
  public interface IBackendClient {
    // Calls the backend's health/echo endpoint.
    Task<PingResponse> PingAsync(PingRequest request, CancellationToken cancellationToken = default);
  }
}
