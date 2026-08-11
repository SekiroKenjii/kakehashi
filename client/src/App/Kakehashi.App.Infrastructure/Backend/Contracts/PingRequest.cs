namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  // Transport-agnostic request for the example health/echo operation.
  // Message: An arbitrary message echoed back by the backend.
  public sealed record PingRequest(string Message);
}
