using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Kakehashi.Interoperability {
  /// <summary>
  /// A static class that provides interop methods for working with windows API.
  /// </summary>
  public static class WindowInterop {
    /// <summary>
    /// Provides strongly-typed constants representing standard Windows Message (WM) identifiers.
    /// </summary>
    public static class WindowMessage {
      /// <summary>
      /// Gets the message identifier sent to both the window being activated and the window being deactivated.
      /// </summary>
      public static uint Activate => PInvoke.WM_ACTIVATE;
    }

    /// <summary>
    /// Provides strongly-typed constants representing Window Activation (WA) states used in Windows messages like WM_ACTIVATE.
    /// </summary>
    public static class WindowActivation {
      /// <summary>
      /// Gets the value indicating that the window is being activated by some method other than a mouse click (for example, by a call to SetActiveWindow or by using the ALT+TAB key combination).
      /// </summary>
      public static uint Active => PInvoke.WA_ACTIVE;

      /// <summary>
      /// Gets the value indicating that the window is being deactivated.
      /// </summary>
      public static uint InActive => PInvoke.WA_INACTIVE;
    }

    /// <summary>
    /// Retrieves the window handle to the active window attached to the calling thread's message queue.
    /// </summary>
    /// <returns>The handle to the active window, or <see cref="IntPtr.Zero"/> if no window is active on the calling thread.</returns>
    public static IntPtr GetActiveWindow() {
      HWND hWnd = PInvoke.GetActiveWindow();
      unsafe { return (nint)hWnd.Value; }
    }

    /// <summary>
    /// Sends the specified message to a window or windows. The method calls the window procedure for the specified window and does not return until the window procedure has processed the message.
    /// </summary>
    /// <param name="hWnd">A handle to the window whose window procedure will receive the message.</param>
    /// <param name="msg">The message to be sent.</param>
    /// <param name="wParam">Additional message-specific information.</param>
    /// <param name="lParam">Additional message-specific information.</param>
    /// <returns>The result of the message processing; its value depends on the message sent.</returns>
    public static IntPtr SendMessage(IntPtr hWnd, uint msg, uint wParam, IntPtr lParam) {
      return PInvoke.SendMessage(new HWND(hWnd), msg, wParam, lParam).Value;
    }

    /// <summary>
    /// Enables or disables mouse and keyboard input to the specified window. While disabled, the window ignores all input, including its caption buttons - the Win32 modal-owner pattern.
    /// </summary>
    /// <param name="hWnd">A handle to the window to enable or disable.</param>
    /// <param name="enable"><see langword="true"/> to enable input; <see langword="false"/> to disable it.</param>
    /// <returns><see langword="true"/> if the window was previously disabled; otherwise <see langword="false"/>.</returns>
    public static bool EnableWindow(IntPtr hWnd, bool enable) {
      return PInvoke.EnableWindow(new HWND(hWnd), enable);
    }

    /// <summary>
    /// Sets the owner of a top-level window, keeping it above its owner in z-order (used together with <see cref="EnableWindow"/> for modal windows).
    /// </summary>
    /// <param name="hWnd">A handle to the window whose owner is set.</param>
    /// <param name="hOwner">A handle to the owner window.</param>
    public static void SetWindowOwner(IntPtr hWnd, IntPtr hOwner) {
      PInvoke.SetWindowLongPtr(new HWND(hWnd), WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, hOwner);
    }
  }
}
