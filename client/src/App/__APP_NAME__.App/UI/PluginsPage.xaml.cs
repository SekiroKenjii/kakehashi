using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

namespace __ROOT_NAMESPACE__.App.UI;

/// <summary>What this composition is made of, and what to do about it.</summary>
public sealed partial class PluginsPage : Page
{
    public PluginsPage(PluginsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    public PluginsViewModel ViewModel { get; }

    /// <summary>Whether a piece of text is worth drawing at all.</summary>
    /// <remarks>
    /// A static helper rather than a converter: x:Bind checks the argument type at compile time and
    /// a converter with the wrong input fails silently on a binding, where nothing points at it.
    /// </remarks>
    public static Visibility HasText(string value)
    {
        return value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static string StagedLabel(string stagedVersion)
    {
        return stagedVersion.Length == 0 ? string.Empty : $"{stagedVersion} staged";
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
    }

    private void OnFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.Filter = sender.SelectedItem?.Text ?? "All";
    }

    private async void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.Tab = sender.SelectedItem?.Text ?? "Installed";

        // Read when it is looked at rather than at load: the catalog is a round trip, and the tab
        // most people open the page for does not need one.
        if (ViewModel.ShowingBrowse)
        {
            await ViewModel.LoadCatalogAsync();
        }
    }

    private async void OnBrowseLocationClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.BrowseForLocationAsync();
    }

    private void OnCreateProjectClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateProject();
    }

    private async void OnBrowseProjectClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.BrowseForProjectAsync();
    }

    private void OnCheckProjectClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CheckProject();
    }

    private void OnPackProjectClick(object sender, RoutedEventArgs e)
    {
        ViewModel.PackProject();
    }

    private async void OnInstallFromFileClick(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.PrepareInstallFromFileAsync())
        {
            await InstallDialog.ShowAsync();
        }
    }

    private async void OnCatalogInstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogListItem item }
            && await ViewModel.PrepareInstallFromCatalogAsync(item))
        {
            await InstallDialog.ShowAsync();
        }
    }

    private async void OnInstallDialogPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // A deferral because accepting a catalog package reports it, and the dialog would otherwise
        // close before the answer arrived.
        var deferral = args.GetDeferral();

        try
        {
            // Cancelling the click keeps the dialog open, so a refused install leaves its reason on
            // the page behind it with what they were agreeing to still on screen.
            args.Cancel = !await ViewModel.ConfirmInstallAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnInstallDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.CancelInstall();
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        // The switch reports the state the user just put it in, and the view model rebuilds the row
        // from what the registry actually did — so a refused change snaps back rather than lying.
        if (sender is ToggleSwitch { Tag: PluginListItem item } toggle && toggle.IsOn != item.IsEnabled)
        {
            ViewModel.Toggle(item);
        }
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PluginListItem item })
        {
            await ViewModel.UninstallAsync(item);
        }
    }

    private void OnRestartClick(object sender, RoutedEventArgs e)
    {
        _ = AppInstance.Restart(string.Empty);
    }
}
