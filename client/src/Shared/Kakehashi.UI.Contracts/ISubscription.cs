using System;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Represents a subscription to a system. It manages the lifecycle of the subscription and
  /// disposes of it when it is not needed anymore.
  /// </summary>
  public interface ISubscription : ITransientDependency, IDisposable {
    /// <summary>
    /// Gets a value indicating whether the subscription has been unsubscribed. Once a subscription is unsubscribed, it should not be used again.
    /// </summary>
    bool Unsubscribed { get; }

    /// <summary>
    /// Add a disposable to the subscription. The disposable will be disposed of when the subscription is unsubscribed.
    /// </summary>
    /// <param name="disposable">The disposable to add to the subscription.</param>
    void Add(IDisposable disposable);

    /// <summary>
    /// Unsubscribes all disposables added to the subscription and marks the subscription as unsubscribed. After calling this method, the subscription should not be used again.
    /// </summary>
    void Unsubscribe();
  }
}
