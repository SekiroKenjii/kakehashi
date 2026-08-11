using System;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// A bag of disposables that are released together.
  /// </summary>
  public interface ISubscription : ITransientDependency, IDisposable {
    /// <summary>Whether <see cref="Unsubscribe"/> has already run.</summary>
    bool Unsubscribed { get; }

    /// <summary>Adds a disposable to release with the rest.</summary>
    /// <param name="disposable">What to release.</param>
    void Add(IDisposable disposable);

    /// <summary>
    /// Releases everything added, once. The subscription is spent afterwards.
    /// </summary>
    void Unsubscribe();
  }
}
