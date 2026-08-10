using System;
using System.Reactive.Disposables;
using Kakehashi.UI.Contracts;

namespace Kakehashi.App.Core {
  public sealed partial class Subscription : ISubscription {
    private readonly CompositeDisposable _disposables = [];

    public bool Unsubscribed => _disposables.IsDisposed;

    public void Add(IDisposable disposable) {
      _disposables.Add(disposable);
    }

    public void Unsubscribe() {
      _disposables.Dispose();
    }

    public void Dispose() {
      if (Unsubscribed) {
        return;
      }

      Dispose(true);
      GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
      if (disposing) {
        _disposables.Dispose();
      }
    }
  }
}
