using System;

namespace __ROOT_NAMESPACE__.Application.Abstractions;

/// <summary>Abstraction over the system clock so handlers stay deterministic and testable.</summary>
public interface IClock
{
    /// <summary>The current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
