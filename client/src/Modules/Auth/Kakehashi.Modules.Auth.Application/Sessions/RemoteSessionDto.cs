using System;

namespace Kakehashi.Modules.Auth.Application.Sessions {
  // A sign-in session on the authorization server, as listed on the account page.
  public sealed record RemoteSessionDto(
      string Id,
      string Client,
      string Device,
      string? IpAddress,
      DateTimeOffset CreatedAt,
      DateTimeOffset LastSeenAt,
      bool IsCurrent);
}
