using System;

namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  // Transport-agnostic reply for the example health/echo operation.
  // Message: The message echoed back by the backend.
  // ServerTimeUtc: The backend's clock at the time the request was handled.
  public sealed record PingResponse(string Message, DateTimeOffset ServerTimeUtc);
}
