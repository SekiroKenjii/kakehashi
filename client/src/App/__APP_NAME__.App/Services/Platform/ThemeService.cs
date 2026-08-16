using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using __ROOT_NAMESPACE__.UI.Common.Helpers;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;

namespace __ROOT_NAMESPACE__.App.Services.Platform;

/// <summary>
/// Applies and persists the app theme. The theme is applied to the main window's content root and
/// the title-bar caption buttons, and stored via <see cref="ILocalSettingsService"/> so it survives
/// restarts (works for the unpackaged app, which has no <c>ApplicationData</c>).
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string _themeSettingKey = "AppTheme";
    private readonly ILocalSettingsService _localSettings;
    private readonly Subject<ElementTheme> _themeChanged = new();

    public ThemeService(ILocalSettingsService localSettings)
    {
        ArgumentNullException.ThrowIfNull(localSettings);
        _localSettings = localSettings;
    }

    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    public IObservable<ElementTheme> OnThemeChanged => _themeChanged.AsObservable();

    public void Initialize()
    {
        Theme = _localSettings.Read<ElementTheme>(_themeSettingKey);

        // When following the system ("Default"), the effective light/dark can change at runtime; keep
        // the caption buttons in sync by re-applying on the root's ActualThemeChanged.
        if (App.MainWindow.Content is FrameworkElement root)
        {
            root.ActualThemeChanged -= OnActualThemeChanged;
            root.ActualThemeChanged += OnActualThemeChanged;
        }

        Apply();
    }

    public void SetTheme(ElementTheme theme)
    {
        Theme = theme;
        _localSettings.Save(_themeSettingKey, theme);
        Apply();
        _themeChanged.OnNext(theme);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        // Only the system-driven case needs this; explicit themes are handled in Apply().
        if (Theme == ElementTheme.Default)
        {
            TitleBarHelper.UpdateTitleBarAppearance(App.MainWindow, ElementTheme.Default);
        }
    }

    private void Apply()
    {
        if (App.MainWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = Theme;
        }

        TitleBarHelper.UpdateTitleBarAppearance(App.MainWindow, Theme);
    }
}
