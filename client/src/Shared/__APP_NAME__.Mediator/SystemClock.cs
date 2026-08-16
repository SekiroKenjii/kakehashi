using System;
using __ROOT_NAMESPACE__.Application.Abstractions;

namespace __ROOT_NAMESPACE__.Mediator;

/// <summary>An <see cref="IClock"/> backed by the operating system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
