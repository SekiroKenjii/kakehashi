using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using __ROOT_NAMESPACE__.UI.Common.Helpers;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace __ROOT_NAMESPACE__.App.Services.Platform;

/// <summary>
/// Applies and persists the accent choice. The project's accent comes from <c>Branding:Accent</c>
/// in configuration — the value the scaffold stamped — and is applied by writing the
/// <c>SystemAccentColor</c> family into application resources, which is where the framework's
/// accent brushes resolve their colours from. Those keys have to be there before the first window
/// resolves one, which is why this service runs ahead of every other startup orchestrator.
/// </summary>
public sealed class AccentService : IAccentService
{
    private const string _accentSettingKey = "AppAccent";

    private readonly ILocalSettingsService _localSettings;
    private readonly IMainWindowProvider _mainWindow;
    private readonly Subject<AccentSource> _accentChanged = new();
    private readonly UISettings _systemColors = new();
    private readonly Color? _appAccent;
    private DispatcherQueue? _dispatcher;

    public AccentService(
        ILocalSettingsService localSettings,
        IMainWindowProvider mainWindow,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(localSettings);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(configuration);

        _localSettings = localSettings;
        _mainWindow = mainWindow;
        _appAccent = AccentPalette.TryParse(configuration["Branding:Accent"], out var color)
            ? color
            : null;
    }

    public AccentSource Accent { get; private set; } = AccentSource.Windows;

    public bool HasAppAccent => _appAccent is not null;

    public IObservable<AccentSource> OnAccentChanged => _accentChanged.AsObservable();

    public void Initialize()
    {
        Accent = _localSettings.Read<AccentSource>(_accentSettingKey);

        // Following Windows is this service's job now that these keys shadow the framework's own
        // accent: nothing else notices the user repainting it in Settings.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _systemColors.ColorValuesChanged += OnSystemColorsChanged;

        Apply();
    }

    public void SetAccent(AccentSource source)
    {
        Accent = source;
        _localSettings.Save(_accentSettingKey, source);
        Apply();
        _accentChanged.OnNext(source);
    }

    private void OnSystemColorsChanged(UISettings sender, object args)
    {
        if (Accent != AccentSource.Windows)
        {
            return;
        }

        // The event arrives on a system thread, and everything Apply touches is the UI thread's.
        _dispatcher?.TryEnqueue(Apply);
    }

    private void Apply()
    {
        var shades = Accent == AccentSource.App && _appAccent is Color accent
            ? AccentPalette.Shades(accent)
            : WindowsShades();

        // Fully qualified because the root namespace has an Application layer, which shadows the
        // XAML type's namespace from inside the App project.
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        resources["SystemAccentColor"] = shades.Base;
        resources["SystemAccentColorLight1"] = shades.Light1;
        resources["SystemAccentColorLight2"] = shades.Light2;
        resources["SystemAccentColorLight3"] = shades.Light3;
        resources["SystemAccentColorDark1"] = shades.Dark1;
        resources["SystemAccentColorDark2"] = shades.Dark2;
        resources["SystemAccentColorDark3"] = shades.Dark3;

        NudgeTheme();
    }

    /// <summary>The ramp Windows derives from the user's accent, which the app now writes itself.</summary>
    private AccentShades WindowsShades()
    {
        return new AccentShades(
            _systemColors.GetColorValue(UIColorType.Accent),
            _systemColors.GetColorValue(UIColorType.AccentLight1),
            _systemColors.GetColorValue(UIColorType.AccentLight2),
            _systemColors.GetColorValue(UIColorType.AccentLight3),
            _systemColors.GetColorValue(UIColorType.AccentDark1),
            _systemColors.GetColorValue(UIColorType.AccentDark2),
            _systemColors.GetColorValue(UIColorType.AccentDark3));
    }

    /// <summary>
    /// Forces already-resolved theme resources to re-resolve by flipping the root's requested
    /// theme away and back, which is the documented way to cause one. Before the shell exists
    /// there is nothing resolved to force, and the write above is the whole job.
    /// </summary>
    private void NudgeTheme()
    {
        if (_mainWindow.MainWindow?.Content is not FrameworkElement root)
        {
            return;
        }

        var requested = root.RequestedTheme;
        root.RequestedTheme = root.ActualTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
        root.RequestedTheme = requested;
    }
}
