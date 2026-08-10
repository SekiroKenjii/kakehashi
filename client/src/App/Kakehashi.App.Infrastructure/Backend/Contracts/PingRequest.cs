namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  /// <summary>Transport-agnostic request for the example health/echo operation.</summary>
  /// <param name="Message">An arbitrary message echoed back by the backend.</param>
  public sealed record PingRequest(string Message);
}
