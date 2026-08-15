using System;
using __ROOT_NAMESPACE__.Modules.Activity.UI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace __ROOT_NAMESPACE__.Modules.Activity.UI.Views;

/// <summary>
/// The activity page: the signed-in account's feed, gathered server-side from every device.
/// </summary>
/// <remarks>
/// Refreshes on open and on demand, never on a timer. See client/docs/architecture.md, "Static
/// helpers on the page, not converters".
/// </remarks>
public sealed partial class ActivityPage : Page
{
    public ActivityPage(ActivityViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;

        InitializeComponent();

        // Wired here, not in XAML: a CollectionViewSource lives in a resource dictionary and x:Bind
        // cannot reach one. The source is the view model's own collection, so regrouping is enough.
        FeedList.ItemsSource = new CollectionViewSource {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath("Items"),
            Source = ViewModel.Days,
        }.View;

        Loaded += OnLoaded;
    }

    public ActivityViewModel ViewModel { get; }

    /// <summary>The icon square's colour: red only for alert rows.</summary>
    public static Brush IconBackground(bool isAlert)
    {
        return Resource(isAlert
            ? "SystemFillColorCriticalBackgroundBrush"
            : "SubtleFillColorSecondaryBrush");
    }

    public static Brush IconForeground(bool isAlert)
    {
        return Resource(isAlert ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
    }

    /// <summary>A chip's fill. The selected one is filled; the rest are outlines.</summary>
    public static Brush ChipBackground(bool isSelected)
    {
        return isSelected
            ? Resource("AccentFillColorDefaultBrush")
            : Resource("ControlFillColorDefaultBrush");
    }

    public static Brush ChipForeground(bool isSelected)
    {
        return isSelected
            ? Resource("TextOnAccentFillColorPrimaryBrush")
            : Resource("TextFillColorSecondaryBrush");
    }

    private static Brush Resource(string key)
    {
        // Fully qualified: this assembly has a __ROOT_NAMESPACE__.Modules.Activity.Application namespace, so a
        // bare Application binds to that rather than to the XAML one.
        if (Microsoft.UI.Xaml.Application.Current.Resources[key] is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    /// <summary>Opens and closes a row.</summary>
    /// <remarks>
    /// <c>ItemClick</c> rather than selection: selecting a row would imply the list has a current item
    /// and that something acts on it, and nothing here does. The clicked item arrives on the event, so
    /// unlike an ItemsRepeater there is no <c>Tag</c> to read it out of.
    /// </remarks>
    private void OnRowClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActivityRow row)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    /// <summary>Applies the search box.</summary>
    /// <remarks>
    /// On submit, not on every keystroke: the search runs on the server. Emptying the box is
    /// handled by the view model, which reloads immediately.
    /// </remarks>
    private async void OnSearchSubmitted(
        AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.SearchCommand.ExecuteAsync(parameter: null);
    }

    /// <summary>Applies the clicked category chip as the feed's filter.</summary>
    /// <remarks>
    /// The row comes off Tag, never DataContext: these templates sit inside an ItemsRepeater,
    /// which does not set DataContext on what it realizes, so a handler reading DataContext would
    /// match nothing and return silently on every click.
    /// </remarks>
    private async void OnChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ActivityChip chip })
        {
            await ViewModel.SelectCategoryCommand.ExecuteAsync(chip);
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ActivityRow row })
        {
            ViewModel.CopyEventCommand.Execute(row);
        }
    }

    private void OnSecureClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SecureAccountCommand.Execute(parameter: null);
    }
}
