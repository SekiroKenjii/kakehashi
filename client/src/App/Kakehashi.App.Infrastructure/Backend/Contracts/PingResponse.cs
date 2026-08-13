using System;

namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  /// <summary>Transport-agnostic reply for the example health/echo operation.</summary>
  public sealed record PingResponse(string Message, DateTimeOffset ServerTimeUtc);
}
