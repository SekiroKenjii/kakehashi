using System;
using Kakehashi.Application.Abstractions;

namespace Kakehashi.Mediator {
  /// <summary>An <see cref="IClock"/> backed by the operating system clock.</summary>
  public sealed class SystemClock : IClock {
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
  }
}
