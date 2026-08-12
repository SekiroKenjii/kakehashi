using Kakehashi.Interoperability;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Kakehashi.UI.Common.Helpers {
  // With ExtendsContentIntoTitleBar the system draws the caption buttons, so their colors live on
  // AppWindow.TitleBar rather than in XAML. ElementTheme.Default has to be resolved to the content
  // root's effective theme first, or the buttons keep stale colors at startup and after a live
  // system-theme switch.
  public static class TitleBarHelper {
    public static void UpdateTitleBarAppearance(WinUIEx.WindowEx window, ElementTheme requestedTheme) {
      var theme = ResolveTheme(window.Content, requestedTheme);
      ApplyCaptionColors(window.AppWindow.TitleBar, theme);
      ForceRedraw(window);
    }

    public static void ApplySystemThemeToCaptionButtons(this WinUIEx.WindowEx window) {
      UpdateTitleBarAppearance(window, ElementTheme.Default);
    }

    private static ElementTheme ResolveTheme(UIElement uiElement, ElementTheme requestedTheme) {
      if (requestedTheme != ElementTheme.Default) {
        return requestedTheme;
      }

      return uiElement is FrameworkElement root
          ? root.ActualTheme
          : ElementTheme.Default;
    }

    private static void ApplyCaptionColors(
        Microsoft.UI.Windowing.AppWindowTitleBar titleBar, ElementTheme theme) {
      bool isDark = theme == ElementTheme.Dark;
      Color foreground = isDark ? Colors.White : Colors.Black;
      Color inactiveForeground = isDark
          ? Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A)
          : Color.FromArgb(0xFF, 0x60, 0x60, 0x60);
      Color hoverBackground = isDark
          ? Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)
          : Color.FromArgb(0x14, 0x00, 0x00, 0x00);
      Color pressedBackground = isDark
          ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
          : Color.FromArgb(0x24, 0x00, 0x00, 0x00);

      // Keep the bar itself transparent so the Mica backdrop shows through.
      titleBar.ButtonBackgroundColor = Colors.Transparent;
      titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
      titleBar.ButtonForegroundColor = foreground;
      titleBar.ButtonInactiveForegroundColor = inactiveForeground;
      titleBar.ButtonHoverForegroundColor = foreground;
      titleBar.ButtonHoverBackgroundColor = hoverBackground;
      titleBar.ButtonPressedForegroundColor = foreground;
      titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }

    private static void ForceRedraw(WinUIEx.WindowEx window) {
      nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

      // The system repaints the caption buttons only on an activation change, hence the WM_ACTIVATE
      // pair; the order depends on which state the window is already in.
      if (hWnd == WindowInterop.GetActiveWindow()) {
        WindowInterop.SendMessage(
          hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.InActive, nint.Zero);
        WindowInterop.SendMessage(
          hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.Active, nint.Zero);
      } else {
        WindowInterop.SendMessage(
          hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.Active, nint.Zero);
        WindowInterop.SendMessage(
          hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.InActive, nint.Zero);
      }
    }
  }
}
