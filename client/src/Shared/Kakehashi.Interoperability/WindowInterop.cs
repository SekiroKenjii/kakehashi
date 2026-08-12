using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Kakehashi.Interoperability {
  public static class WindowInterop {
    public static class WindowMessage {
      // WM_ACTIVATE arrives at both the window being activated and the one being deactivated.
      public static uint Activate => PInvoke.WM_ACTIVATE;
    }

    public static class WindowActivation {
      // Does not cover mouse-click activation; that arrives as WA_CLICKACTIVE, not exposed here.
      public static uint Active => PInvoke.WA_ACTIVE;

      public static uint InActive => PInvoke.WA_INACTIVE;
    }

    // Only sees the active window of the calling thread's message queue; zero for any other thread.
    public static IntPtr GetActiveWindow() {
      HWND hWnd = PInvoke.GetActiveWindow();
      unsafe { return (nint)hWnd.Value; }
    }

    // Blocks until the target window procedure has processed the message, unlike PostMessage.
    public static IntPtr SendMessage(IntPtr hWnd, uint msg, uint wParam, IntPtr lParam) {
      return PInvoke.SendMessage(new HWND(hWnd), msg, wParam, lParam).Value;
    }

    // Half of the Win32 modal-owner pattern: a disabled window ignores all input, caption buttons
    // included. Returns whether the window was already disabled, not whether the call succeeded.
    public static bool EnableWindow(IntPtr hWnd, bool enable) {
      return PInvoke.EnableWindow(new HWND(hWnd), enable);
    }

    // Keeps the window above its owner in z-order; pair with EnableWindow to get a modal window.
    public static void SetWindowOwner(IntPtr hWnd, IntPtr hOwner) {
      PInvoke.SetWindowLongPtr(new HWND(hWnd), WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, hOwner);
    }
  }
}
