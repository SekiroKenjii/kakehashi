using System;

namespace Kakehashi.UI.Contracts {
  public interface ISubscription : ITransientDependency, IDisposable {
    bool Unsubscribed { get; }

    void Add(IDisposable disposable);

    // Runs once: the subscription is spent afterwards, not reusable.
    void Unsubscribe();
  }
}
