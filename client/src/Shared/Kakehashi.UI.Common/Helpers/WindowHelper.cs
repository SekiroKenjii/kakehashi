using System;
using System.IO;
using Kakehashi.Interoperability;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Kakehashi.UI.Common.Helpers {
  public static class WindowHelper {
    // A missing icon file is ignored rather than thrown, so startup never fails on a rebrand.
    public static void TrySetAppIcon(Window window) {
      ArgumentNullException.ThrowIfNull(window);

      string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
      if (File.Exists(iconPath)) {
        window.AppWindow.SetIcon(iconPath);
      }
    }

    // All input to the owner, its caption buttons included, stays disabled until the returned
    // handle is disposed; drop it on the floor and the owner is stuck.
    public static IDisposable ShowModalOver(Window window, Window owner) {
      ArgumentNullException.ThrowIfNull(window);
      ArgumentNullException.ThrowIfNull(owner);

      nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
      nint hOwner = WinRT.Interop.WindowNative.GetWindowHandle(owner);

      WindowInterop.SetWindowOwner(hWnd, hOwner);
      WindowInterop.EnableWindow(hOwner, enable: false);

      PointInt32 ownerPosition = owner.AppWindow.Position;
      SizeInt32 ownerSize = owner.AppWindow.Size;
      SizeInt32 size = window.AppWindow.Size;
      window.AppWindow.Move(new PointInt32(
          ownerPosition.X + ((ownerSize.Width - size.Width) / 2),
          ownerPosition.Y + ((ownerSize.Height - size.Height) / 2)));

      window.Activate();
      return new ModalSession(owner, hOwner);
    }

    private sealed class ModalSession : IDisposable {
      private readonly Window _owner;
      private readonly nint _hOwner;
      private bool _disposed;

      public ModalSession(Window owner, nint hOwner) {
        _owner = owner;
        _hOwner = hOwner;
      }

      public void Dispose() {
        if (_disposed) {
          return;
        }

        _disposed = true;
        WindowInterop.EnableWindow(_hOwner, enable: true);
        _owner.Activate();
      }
    }
  }
}
