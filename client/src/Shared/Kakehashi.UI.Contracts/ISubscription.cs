using System;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Collects <see cref="IDisposable"/>s and disposes them together when the subscription ends.
  /// </summary>
  public interface ISubscription : ITransientDependency, IDisposable {
    /// <summary>
    /// Whether <see cref="Unsubscribe"/> (or Dispose) has run; a spent subscription must not be
    /// reused.
    /// </summary>
    bool Unsubscribed { get; }

    /// <summary>Adds a disposable to dispose when the subscription ends.</summary>
    void Add(IDisposable disposable);

    /// <summary>
    /// Disposes everything added and marks the subscription <see cref="Unsubscribed"/>.
    /// </summary>
    void Unsubscribe();
  }
}
