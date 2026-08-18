using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;

namespace __ROOT_NAMESPACE__.App.UI;

/// <summary>
/// Presents the theme choice as a 0/1/2 index and the accent choice as a 0/1 index, and applies
/// each through its service.
/// </summary>
public sealed partial class SettingsViewModel : ViewModel
{
    private readonly IThemeService _themeService;
    private readonly IAccentService _accentService;

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int AccentIndex { get; set; }

    /// <summary>
    /// Whether the accent card is shown at all. A project scaffolded without an accent has one
    /// working choice, and a switch that cannot switch is not offered.
    /// </summary>
    public bool HasAccentChoice { get; }

    public SettingsViewModel(IThemeService themeService, IAccentService accentService)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(accentService);
        _themeService = themeService;
        _accentService = accentService;
        ThemeIndex = themeService.Theme switch {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
        AccentIndex = accentService.Accent == AccentSource.App ? 1 : 0;
        HasAccentChoice = accentService.HasAppAccent;
    }

    partial void OnThemeIndexChanged(int value)
    {
        _themeService.SetTheme(value switch {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        });
    }

    partial void OnAccentIndexChanged(int value)
    {
        _accentService.SetAccent(value == 1 ? AccentSource.App : AccentSource.Windows);
    }

    /// <summary>Opens the folder the application writes its log to.</summary>
    /// <remarks>
    /// This replaces a switch labelled "Error reporting" whose handler was empty — nothing sent, and
    /// not even the flag persisted. Opening the folder is a smaller promise and an honest one, and it
    /// answers the question that actually follows a crash: where is the log.
    /// </remarks>
    [RelayCommand]
    private void OpenLogFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "__APP_NAME__",
            "logs");
        Directory.CreateDirectory(folder);

        // UseShellExecute, because this is a folder for Explorer rather than a program to run.
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }
}
