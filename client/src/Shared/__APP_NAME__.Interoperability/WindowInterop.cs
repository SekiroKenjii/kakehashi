using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace __ROOT_NAMESPACE__.Interoperability;

public static class WindowInterop
{
    /// <summary>
    /// Windows Message (WM) identifiers, typed for use with <see cref="SendMessage"/>.
    /// </summary>
    public static class WindowMessage
    {
        /// <summary>
        /// Sent to both the window being activated and the window being deactivated.
        /// </summary>
        public static uint Activate => PInvoke.WM_ACTIVATE;
    }

    /// <summary>Window Activation (WA) states carried by <c>WM_ACTIVATE</c>.</summary>
    public static class WindowActivation
    {
        /// <summary>
        /// Activated by some method other than a mouse click (for example SetActiveWindow or
        /// Alt+Tab).
        /// </summary>
        public static uint Active => PInvoke.WA_ACTIVE;

        public static uint InActive => PInvoke.WA_INACTIVE;
    }

    /// <summary>
    /// Retrieves the window handle to the active window attached to the calling thread's message queue.
    /// </summary>
    /// <returns>The handle to the active window, or <see cref="IntPtr.Zero"/> if no window is active on the calling thread.</returns>
    public static IntPtr GetActiveWindow()
    {
        HWND hWnd = PInvoke.GetActiveWindow();
        unsafe { return (nint)hWnd.Value; }
    }

    /// <summary>
    /// Sends a message to a window, returning only after the window procedure has processed it.
    /// </summary>
    public static IntPtr SendMessage(IntPtr hWnd, uint msg, uint wParam, IntPtr lParam)
    {
        return PInvoke.SendMessage(new HWND(hWnd), msg, wParam, lParam).Value;
    }

    /// <summary>
    /// Enables or disables mouse and keyboard input to the window. While disabled, the window
    /// ignores all input, including its caption buttons - the Win32 modal-owner pattern.
    /// </summary>
    /// <returns><see langword="true"/> if the window was disabled before the call.</returns>
    public static bool EnableWindow(IntPtr hWnd, bool enable)
    {
        return PInvoke.EnableWindow(new HWND(hWnd), enable);
    }

    /// <summary>
    /// Sets the owner of a top-level window, keeping it above its owner in z-order (pairs with
    /// <see cref="EnableWindow"/> for modal windows).
    /// </summary>
    public static void SetWindowOwner(IntPtr hWnd, IntPtr hOwner)
    {
        PInvoke.SetWindowLongPtr(new HWND(hWnd), WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, hOwner);
    }
}
