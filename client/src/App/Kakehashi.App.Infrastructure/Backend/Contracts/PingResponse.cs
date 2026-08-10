using System;

namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  /// <summary>Transport-agnostic reply for the example health/echo operation.</summary>
  /// <param name="Message">The message echoed back by the backend.</param>
  /// <param name="ServerTimeUtc">The backend's clock at the time the request was handled.</param>
  public sealed record PingResponse(string Message, DateTimeOffset ServerTimeUtc);
}
