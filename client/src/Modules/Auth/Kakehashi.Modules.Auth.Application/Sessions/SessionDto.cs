using System;
using System.Collections.Generic;

namespace Kakehashi.Modules.Auth.Application.Sessions {
  // A flat read model describing the current authentication state for presentation.
  public sealed record SessionDto(
      bool IsAuthenticated,
      string? DisplayName,
      string? Email,
      DateTimeOffset? ExpiresAtUtc,
      DateTimeOffset? SignedInAtUtc,
      IReadOnlyList<string> Roles);
}
