using System;

namespace Kakehashi.App.Infrastructure.Backend.Contracts {
  public sealed record PingResponse(string Message, DateTimeOffset ServerTimeUtc);
}
