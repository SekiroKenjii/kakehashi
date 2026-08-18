using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using __ROOT_NAMESPACE__.UI.Common.Helpers;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace __ROOT_NAMESPACE__.App.Services.Platform;

/// <summary>
/// Applies and persists the accent choice. The project's accent comes from <c>Branding:Accent</c>
/// in configuration — the value the scaffold stamped — and is applied by overriding the
/// <c>SystemAccentColor</c> family in application resources, which is where the framework's accent
/// brushes resolve their colours from.
/// </summary>
public sealed class AccentService : IAccentService
{
    private const string _accentSettingKey = "AppAccent";

    private static readonly string[] _resourceKeys = [
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    private readonly ILocalSettingsService _localSettings;
    private readonly Subject<AccentSource> _accentChanged = new();
    private readonly Color? _appAccent;

    public AccentService(ILocalSettingsService localSettings, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(localSettings);
        ArgumentNullException.ThrowIfNull(configuration);
        _localSettings = localSettings;
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

        // Nothing to undo on a fresh start: the overrides exist only once this service writes
        // them, so the Windows choice needs no apply and no theme nudge.
        if (Accent == AccentSource.App)
        {
            Apply();
        }
    }

    public void SetAccent(AccentSource source)
    {
        Accent = source;
        _localSettings.Save(_accentSettingKey, source);
        Apply();
        _accentChanged.OnNext(source);
    }

    private void Apply()
    {
        // Fully qualified because the root namespace has an Application layer, which shadows the
        // XAML type's namespace from inside the App project.
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;

        if (Accent == AccentSource.App && _appAccent is Color accent)
        {
            var shades = AccentPalette.Shades(accent);
            resources["SystemAccentColor"] = shades.Base;
            resources["SystemAccentColorLight1"] = shades.Light1;
            resources["SystemAccentColorLight2"] = shades.Light2;
            resources["SystemAccentColorLight3"] = shades.Light3;
            resources["SystemAccentColorDark1"] = shades.Dark1;
            resources["SystemAccentColorDark2"] = shades.Dark2;
            resources["SystemAccentColorDark3"] = shades.Dark3;
        }
        else
        {
            // Removing the overrides is what restores the Windows accent: lookups fall back to
            // the values the framework seeded.
            foreach (var key in _resourceKeys)
            {
                resources.Remove(key);
            }
        }

        NudgeTheme();
    }

    /// <summary>
    /// Forces already-resolved theme resources to re-resolve by flipping the root's requested
    /// theme away and back. The accent brushes were baked when the window first rendered; there
    /// is no re-evaluate call, and this round trip is the documented way to cause one.
    /// </summary>
    private static void NudgeTheme()
    {
        if (App.MainWindow.Content is not FrameworkElement root)
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
