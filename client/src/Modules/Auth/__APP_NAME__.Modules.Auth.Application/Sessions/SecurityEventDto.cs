using System;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions;

/// <summary>An entry in the user's security activity feed on the authorization server.</summary>
public sealed record SecurityEventDto(
    string Kind,
    string? Device,
    string? IpAddress,
    DateTimeOffset OccurredAt);
