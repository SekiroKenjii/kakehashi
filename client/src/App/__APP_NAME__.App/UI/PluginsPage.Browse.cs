using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.App.Services;

namespace __ROOT_NAMESPACE__.App.UI;

/// <summary>One row of the catalog list.</summary>
/// <param name="Plugin">What the catalog offers, which is what an install is started from.</param>
/// <param name="State">
/// What this installation has already done about it, so a row never offers an install that would
/// be refused or a download of something already here.
/// </param>
public sealed record CatalogListItem(CatalogPlugin Plugin, CatalogItemState State)
{
    public string Meta => string.Join(" · ", new[] {
        Plugin.Publisher,
        Size(Plugin.SizeInBytes),
        Plugin.PublishedAt == default
            ? string.Empty
            : $"published {Plugin.PublishedAt.LocalDateTime:yyyy-MM-dd}",
        $"needs host SDK v{Plugin.MinHostSdk}",
    }.Where(part => part.Length > 0));

    public string StateLabel => State switch {
        CatalogItemState.Installed => "Installed",
        CatalogItemState.UpdateAvailable => "Update",
        CatalogItemState.Staged => "Staged",
        _ => "Install",
    };

    /// <summary>Only what is not already here, in this version, can be installed.</summary>
    public bool CanInstall => State is CatalogItemState.Available or CatalogItemState.UpdateAvailable;

    private static string Size(long bytes)
    {
        return bytes <= 0
            ? string.Empty
            : string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024d / 1024d:0.0} MB");
    }
}

/// <summary>What this installation has already done about a catalog entry.</summary>
public enum CatalogItemState
{
    /// <summary>Not installed.</summary>
    Available,

    /// <summary>Installed, at the version the catalog offers.</summary>
    Installed,

    /// <summary>Installed, at an older version than the catalog offers.</summary>
    UpdateAvailable,

    /// <summary>Downloaded and waiting for the next launch.</summary>
    Staged,
}

/// <summary>
/// The Browse tab: what this deployment offers, and installing one of it.
/// </summary>
/// <remarks>
/// A catalog install goes through the same prompt a file does. The catalog says who publishes a
/// package, and the digest says the bytes are the ones it published — neither says the code was
/// reviewed, so a package this application's own publisher did not sign is asked about wherever it
/// came from.
/// </remarks>
public sealed partial class PluginsViewModel
{
    [ObservableProperty]
    private bool _isCatalogBusy;

    [ObservableProperty]
    private string _catalogMessage = string.Empty;

    public ObservableCollection<CatalogListItem> CatalogItems { get; } = [];

    public bool HasCatalogMessage => CatalogMessage.Length > 0;

    /// <summary>Reads the catalog. Anything that goes wrong is a message where the list would be.</summary>
    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        IsCatalogBusy = true;
        CatalogMessage = string.Empty;

        try
        {
            var listed = await _catalogService.ListAsync(CancellationToken.None);

            CatalogItems.Clear();

            if (listed.IsFailure)
            {
                CatalogMessage = listed.Error.Message;

                return;
            }

            foreach (var plugin in listed.Value)
            {
                CatalogItems.Add(new CatalogListItem(plugin, StateOf(plugin)));
            }

            if (CatalogItems.Count == 0)
            {
                CatalogMessage = "This deployment offers no plugins yet.";
            }
        }
        finally
        {
            IsCatalogBusy = false;
        }
    }

    /// <summary>
    /// Downloads one entry and holds it while the user decides, exactly as a file install does.
    /// </summary>
    /// <remarks>
    /// The archive is a temporary file and nothing installs from it: <see cref="PluginInstaller"/>
    /// unpacks it into staging while it is open, and what the user then agrees to is what was
    /// unpacked.
    /// </remarks>
    public async Task<bool> PrepareInstallFromCatalogAsync(CatalogListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{item.Plugin.PluginID}-{item.Plugin.Version}{PluginPaths.PackageExtension}");
        IsCatalogBusy = true;

        try
        {
            var downloaded = await _catalogService.DownloadAsync(
                item.Plugin, path, CancellationToken.None);

            if (downloaded.IsFailure)
            {
                return Refuse(downloaded.Error.Message);
            }

            return Prepare(path, _catalogSource);
        }
        finally
        {
            IsCatalogBusy = false;
            Delete(path);
        }
    }

    private CatalogItemState StateOf(CatalogPlugin plugin)
    {
        if (_catalog.AwaitingRestart.Any(record => Same(record.PluginID, plugin.PluginID)))
        {
            return CatalogItemState.Staged;
        }

        var installed = _catalog.Loaded.FirstOrDefault(
            loaded => Same(loaded.Record.PluginID, plugin.PluginID));

        if (installed is null)
        {
            return CatalogItemState.Available;
        }

        return Same(installed.Record.InstalledVersion, plugin.Version)
            ? CatalogItemState.Installed
            : CatalogItemState.UpdateAvailable;
    }

    private static bool Same(string left, string right)
    {
        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The download lives in the temporary directory and nothing reads it again: what an
            // install works from is what was unpacked out of it.
        }
    }

    partial void OnCatalogMessageChanged(string value) => OnPropertyChanged(nameof(HasCatalogMessage));
}
