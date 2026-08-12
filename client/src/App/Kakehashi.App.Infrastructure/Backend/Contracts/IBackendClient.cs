using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  // Transport-agnostic gateway: callers never depend on a concrete transport, and the registered
  // implementation is chosen from BackendOptions.Protocol. New operations must be implemented in
  // both HttpBackendClient and GrpcBackendClient.
  public interface IBackendClient {
    Task<PingResponse> PingAsync(PingRequest request, CancellationToken cancellationToken = default);
  }
}
