using System;

namespace Kakehashi.Application.Abstractions {
  // Injected so handlers never read DateTimeOffset.UtcNow directly and stay deterministic in tests.
  public interface IClock {
    DateTimeOffset UtcNow { get; }
  }
}
