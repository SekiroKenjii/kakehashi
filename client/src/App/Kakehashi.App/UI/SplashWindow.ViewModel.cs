using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.App.Infrastructure.Backend;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.Options;

namespace Kakehashi.App.UI {
  public sealed partial class SplashViewModel : ViewModel {
    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string StepText { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    public string BackendText { get; }

    public bool HasBackend => BackendText.Length > 0;

    public string VersionText { get; }

    public SplashViewModel(IOptions<BackendOptions> backendOptions) {
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

    // step is 1-based.
    public void ReportProgress(int step, int totalSteps, string statusText) {
      StatusText = statusText;
      StepText = $"{step} / {totalSteps}";
      IsIndeterminate = false;
      ProgressValue = totalSteps == 0 ? 0 : 100.0 * step / totalSteps;
    }

    // The build identifier is the source commit the SDK stamps into the informational version's +
    // metadata (SourceLink's SourceRevisionId); local builds without one show just the version.
    private static string ResolveVersionText() {
      var assembly = Assembly.GetEntryAssembly();
      var version = assembly?.GetName().Version;
      string versionText = version is null
          ? string.Empty
          : $"v{version.Major}.{version.Minor}.{version.Build}";

      string? informational = assembly
          ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
          ?.InformationalVersion;
      int metadataStart = informational?.IndexOf('+') ?? -1;
      if (metadataStart < 0) {
        return versionText;
      }

      string commit = informational![(metadataStart + 1)..];
      if (commit.Length > 7) {
        commit = commit[..7];
      }
      return $"{versionText} · build {commit}";
    }
  }
}
