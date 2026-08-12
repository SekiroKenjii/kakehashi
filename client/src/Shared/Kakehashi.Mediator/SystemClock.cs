using System;
using Kakehashi.Application.Abstractions;

namespace Kakehashi.Mediator {
  public sealed class SystemClock : IClock {
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
  }
}
