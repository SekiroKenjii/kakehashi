using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.App.Infrastructure.Backend;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.Options;

namespace Kakehashi.App.UI;

public sealed partial class SplashViewModel : ViewModel
{
    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string StepText { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    /// <summary>The backend endpoint shown in the footer, e.g. <c>localhost:5001 · connecting</c>.</summary>
    public string BackendText { get; }

    public bool HasBackend => BackendText.Length > 0;

    /// <summary>The application version shown in the footer, e.g. <c>v1.0.0 · build 0</c>.</summary>
    public string VersionText { get; }

    public SplashViewModel(IOptions<BackendOptions> backendOptions)
    {
        ArgumentNullException.ThrowIfNull(backendOptions);

        StatusText = "Starting...";
        StepText = string.Empty;
        IsIndeterminate = true;

        BackendText =
            Uri.TryCreate(backendOptions.Value.BaseAddress, UriKind.Absolute, out var backendUri)
                ? $"{backendUri.Authority} · connecting"
                : string.Empty;

        VersionText = ResolveVersionText();
    }

    /// <summary>Reports startup progress: the 1-based step, the total step count and a status text.</summary>
    public void ReportProgress(int step, int totalSteps, string statusText)
    {
        StatusText = statusText;
        StepText = $"{step} / {totalSteps}";
        IsIndeterminate = false;
        ProgressValue = totalSteps == 0 ? 0 : 100.0 * step / totalSteps;
    }

    /// <summary>
    /// Builds the footer version, e.g. <c>v0.4.0 · build 05609c6</c>. The build identifier is the
    /// source commit the SDK stamps into the informational version's <c>+</c> metadata
    /// (SourceLink's SourceRevisionId); local builds without one show just the version.
    /// </summary>
    private static string ResolveVersionText()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        string versionText = version is null
            ? string.Empty
            : $"v{version.Major}.{version.Minor}.{version.Build}";

        string? informational = assembly
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        int metadataStart = informational?.IndexOf('+') ?? -1;

        if (metadataStart < 0)
        {
            return versionText;
        }

        string commit = informational![(metadataStart + 1)..];

        if (commit.Length > 7)
        {
            commit = commit[..7];
        }

        return $"{versionText} · build {commit}";
    }
}
