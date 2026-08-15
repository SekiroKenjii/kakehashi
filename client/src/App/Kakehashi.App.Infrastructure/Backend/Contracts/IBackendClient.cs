using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Infrastructure.Backend.Contracts;

/// <summary>
/// Transport-agnostic gateway to the separate backend service. Callers depend on this interface and
/// never on a concrete transport; the registered implementation (HTTP or gRPC) is chosen from
/// <see cref="BackendOptions.Protocol"/>. Add backend operations here and implement them in both
/// <c>HttpBackendClient</c> and <c>GrpcBackendClient</c>.
/// </summary>
public interface IBackendClient
{
    /// <summary>Calls the backend's health/echo endpoint.</summary>
    Task<PingResponse> PingAsync(PingRequest request, CancellationToken cancellationToken = default);
}
