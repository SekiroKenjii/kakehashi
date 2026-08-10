using System;

namespace Kakehashi.Application.Abstractions {
  /// <summary>Abstraction over the system clock so handlers stay deterministic and testable.</summary>
  public interface IClock {
    DateTimeOffset UtcNow { get; }
  }
}
