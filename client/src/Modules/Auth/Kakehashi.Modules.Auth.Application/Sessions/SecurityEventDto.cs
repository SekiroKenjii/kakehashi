using System;

namespace Kakehashi.Modules.Auth.Application.Sessions {
  public sealed record SecurityEventDto(
      string Kind,
      string? Device,
      string? IpAddress,
      DateTimeOffset OccurredAt);
}
