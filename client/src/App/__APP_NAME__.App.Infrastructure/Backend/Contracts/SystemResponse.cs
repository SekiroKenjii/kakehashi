using System;
using System.Collections.Generic;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Backend.Contracts;

/// <summary>One dependency the server needs and whether it answered just now.</summary>
public sealed record SystemDependency(string Name, bool IsOk, long LatencyMs);

/// <summary>
/// Transport-agnostic reply for the system status operation: the process, and one entry per
/// dependency in the server's own order.
/// </summary>
public sealed record SystemResponse(
    string Version,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ServerTimeUtc,
    IReadOnlyList<SystemDependency> Dependencies);
