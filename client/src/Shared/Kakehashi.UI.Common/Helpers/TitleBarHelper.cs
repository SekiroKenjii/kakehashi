using Kakehashi.Interoperability;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Kakehashi.UI.Common.Helpers;

/// <summary>
/// Themes the title bar's system caption buttons. With <c>ExtendsContentIntoTitleBar</c> the caption
/// buttons are drawn by the system, so their colors are set on <c>AppWindow.TitleBar</c>. When the
/// app theme is <see cref="ElementTheme.Default"/> the buttons must follow the *effective* theme
/// (resolved from the content root's <see cref="FrameworkElement.ActualTheme"/>), otherwise they
/// keep stale colors at startup and after a live system-theme switch.
/// </summary>
public static class TitleBarHelper
{
    /// <summary>
    /// Applies caption-button colors for the given requested theme, resolving
    /// <see cref="ElementTheme.Default"/> to the content root's actual (effective) theme.
    /// </summary>
    public static void UpdateTitleBarAppearance(WinUIEx.WindowEx window, ElementTheme requestedTheme)
    {
        var theme = ResolveTheme(window.Content, requestedTheme);
        ApplyCaptionColors(window.AppWindow.TitleBar, theme);
        ForceRedraw(window);
    }

    /// <summary>Applies caption-button colors that follow the system/effective theme.</summary>
    public static void ApplySystemThemeToCaptionButtons(this WinUIEx.WindowEx window)
    {
        UpdateTitleBarAppearance(window, ElementTheme.Default);
    }

    private static ElementTheme ResolveTheme(UIElement uiElement, ElementTheme requestedTheme)
    {
        if (requestedTheme != ElementTheme.Default)
        {
            return requestedTheme;
        }

        // Follow the effective theme of the content root (which itself follows the OS when Default).
        return uiElement is FrameworkElement root
            ? root.ActualTheme
            : ElementTheme.Default;
    }

    private static void ApplyCaptionColors(
        Microsoft.UI.Windowing.AppWindowTitleBar titleBar, ElementTheme theme)
    {
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

    private static void ForceRedraw(WinUIEx.WindowEx window)
    {
        nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        // Force the system to redraw the caption buttons
        if (hWnd == WindowInterop.GetActiveWindow())
        {
            WindowInterop.SendMessage(
              hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.InActive, nint.Zero);
            WindowInterop.SendMessage(
              hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.Active, nint.Zero);
        }
        else
        {
            WindowInterop.SendMessage(
              hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.Active, nint.Zero);
            WindowInterop.SendMessage(
              hWnd, WindowInterop.WindowMessage.Activate, WindowInterop.WindowActivation.InActive, nint.Zero);
        }
    }
}
