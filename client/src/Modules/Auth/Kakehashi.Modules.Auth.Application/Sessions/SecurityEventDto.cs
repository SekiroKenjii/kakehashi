using System;

namespace Kakehashi.Modules.Auth.Application.Sessions {
  // An entry in the user's security activity feed on the authorization server.
  public sealed record SecurityEventDto(
      string Kind,
      string? Device,
      string? IpAddress,
      DateTimeOffset OccurredAt);
}
