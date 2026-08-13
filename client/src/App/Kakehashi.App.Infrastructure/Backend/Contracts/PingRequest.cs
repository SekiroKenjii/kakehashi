namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  /// <summary>Transport-agnostic request for the example health/echo operation.</summary>
  public sealed record PingRequest(string Message);
}
