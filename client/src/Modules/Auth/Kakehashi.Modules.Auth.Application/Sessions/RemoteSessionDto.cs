using System;

namespace Kakehashi.Modules.Auth.Application.Sessions {
  public sealed record RemoteSessionDto(
      string Id,
      string Client,
      string Device,
      string? IpAddress,
      DateTimeOffset CreatedAt,
      DateTimeOffset LastSeenAt,
      bool IsCurrent);
}
