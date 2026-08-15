using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;

namespace Kakehashi.App.UI;

/// <summary>Presents the theme choice as a 0/1/2 index and applies it through the theme service.</summary>
public sealed partial class SettingsViewModel : ViewModel
{
    private readonly IThemeService _themeService;

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public SettingsViewModel(IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        _themeService = themeService;
        ThemeIndex = themeService.Theme switch {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
    }

    partial void OnThemeIndexChanged(int value)
    {
        _themeService.SetTheme(value switch {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        });
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
            "Kakehashi",
            "logs");
        Directory.CreateDirectory(folder);

        // UseShellExecute, because this is a folder for Explorer rather than a program to run.
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }
}
