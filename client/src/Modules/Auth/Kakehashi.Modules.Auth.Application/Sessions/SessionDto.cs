using System;
using System.Collections.Generic;

namespace Kakehashi.Modules.Auth.Application.Sessions {
  public sealed record SessionDto(
      bool IsAuthenticated,
      string? DisplayName,
      string? Email,
      DateTimeOffset? ExpiresAtUtc,
      DateTimeOffset? SignedInAtUtc,
      IReadOnlyList<string> Roles);
}
