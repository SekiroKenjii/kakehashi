using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Base class for view models: change notification plus a navigation and disposal lifecycle.
  /// </summary>
  public abstract class ViewModel : ObservableObject, IDisposable {
    private bool _disposed;

    /// <summary>
    /// Called when the page showing this view model is navigated to, with the navigation argument.
    /// </summary>
    /// <remarks>
    /// Not raised by <c>Frame.Navigate</c>: pages come from the container and the navigation
    /// service sets <c>Frame.Content</c> directly, so a page that needs to load on first display
    /// does so from <c>Loaded</c> instead — see docs/adr/0011-pages-load-on-loaded-not-onnavigatedto.md.
    /// </remarks>
    public virtual void OnNavigatedTo(params ReadOnlySpan<object> args) { }

    /// <summary>Called when the page showing this view model is navigated away from.</summary>
    public virtual void OnNavigatedFrom() { }

    /// <summary>Releases what the view model holds. Safe to call more than once.</summary>
    public void Dispose() {
      if (_disposed) {
        return;
      }

      _disposed = true;
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Override to release derived state; runs once.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing) { /* implement from derived classes */ }
  }
}
