using System;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Backend.Contracts;

/// <summary>Transport-agnostic reply for the example health/echo operation.</summary>
public sealed record PingResponse(string Message, DateTimeOffset ServerTimeUtc);
