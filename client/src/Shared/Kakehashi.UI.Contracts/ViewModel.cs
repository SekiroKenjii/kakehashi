using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kakehashi.UI.Contracts {
  public abstract class ViewModel : ObservableObject, IDisposable {
    private bool _disposed;

    public virtual void OnNavigatedTo(params ReadOnlySpan<object> args) { }

    public virtual void OnNavigatedFrom() { }

    public void Dispose() {
      if (_disposed) {
        return;
      }

      _disposed = true;
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { /* implement from derived classes */ }
  }
}
