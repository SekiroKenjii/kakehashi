using System;
using System.Collections.Generic;

namespace Kakehashi.Modules.Auth.Application.Sessions;

/// <summary>A flat read model describing the current authentication state for presentation.</summary>
public sealed record SessionDto(
    bool IsAuthenticated,
    string? DisplayName,
    string? Email,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? SignedInAtUtc,
    IReadOnlyList<string> Roles);
