using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Infrastructure.Backend.Contracts;
// Aliased, because the generated PingRequest/PingResponse would otherwise collide with the
// transport-agnostic contracts of the same name. The alias is "HealthV1" rather than "Health"
// on purpose: inside a Kakehashi.* namespace, the identifier "Health" binds to the enclosing
// Kakehashi.Health namespace before it ever reaches a using alias.
using HealthV1 = Kakehashi.Health.V1;

namespace Kakehashi.App.Infrastructure.Backend.Grpc {
  /// <summary>
  /// gRPC implementation of <see cref="IBackendClient"/>. It wraps the strongly-typed client
  /// generated from <c>proto/kakehashi/health/v1/health.proto</c> — the same file the Go server
  /// compiles against, so the two halves cannot disagree about the contract — and maps between the
  /// generated protobuf messages and the transport-agnostic contracts. Calls are traced
  /// automatically by the OpenTelemetry gRPC-client instrumentation.
  /// </summary>
  public sealed class GrpcBackendClient : IBackendClient {
    private readonly HealthV1.HealthService.HealthServiceClient _client;

    public GrpcBackendClient(HealthV1.HealthService.HealthServiceClient client) {
      _client = client;
    }

    public async Task<PingResponse> PingAsync(
        PingRequest request, CancellationToken cancellationToken = default) {
      var reply = await _client
          .PingAsync(
              new HealthV1.PingRequest { Message = request.Message },
              cancellationToken: cancellationToken)
          .ResponseAsync
          .ConfigureAwait(false);

      return new PingResponse(reply.Message, reply.ServerTime.ToDateTimeOffset());
    }
  }
}
