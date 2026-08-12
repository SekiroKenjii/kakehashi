using System;
using Kakehashi.Modules.Activity.UI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Kakehashi.Modules.Activity.UI.Views {
  // Refreshes when opened and when asked, never on a timer: a poll would keep running while the
  // window is minimised and the machine locked, filling the server's request log with calls nobody
  // is looking at, and this page is not a live monitor.
  //
  // The static helpers exist because x:Bind calls functions but cannot choose a brush from a bool.
  // They are on the page rather than in converters because a function is compile-checked against
  // its arguments and a converter is not.
  public sealed partial class ActivityPage : Page {
    public ActivityPage(ActivityViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      // Grouping is wired here rather than in XAML because a CollectionViewSource lives in a
      // resource dictionary and x:Bind cannot reach into one. The source is the view model's own
      // observable collection, so re-grouping a page updates the list without touching this again.
      FeedList.ItemsSource = new CollectionViewSource {
        IsSourceGrouped = true,
        ItemsPath = new PropertyPath("Items"),
        Source = ViewModel.Days,
      }.View;

      Loaded += OnLoaded;
    }

    public ActivityViewModel ViewModel { get; }

    public static Brush IconBackground(bool isAlert) {
      return Resource(isAlert
          ? "SystemFillColorCriticalBackgroundBrush"
          : "SubtleFillColorSecondaryBrush");
    }

    public static Brush IconForeground(bool isAlert) {
      return Resource(isAlert ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
    }

    public static Brush ChipBackground(bool isSelected) {
      return isSelected
          ? Resource("AccentFillColorDefaultBrush")
          : Resource("ControlFillColorDefaultBrush");
    }

    public static Brush ChipForeground(bool isSelected) {
      return isSelected
          ? Resource("TextOnAccentFillColorPrimaryBrush")
          : Resource("TextFillColorSecondaryBrush");
    }

    private static Brush Resource(string key) {
      // Fully qualified: this assembly has a Kakehashi.Modules.Activity.Application namespace, so a
      // bare Application binds to that rather than to the XAML one.
      if (Microsoft.UI.Xaml.Application.Current.Resources[key] is Brush brush) {
        return brush;
      }
      return new SolidColorBrush(Colors.Transparent);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    // ItemClick rather than selection: selecting a row would imply the list has a current item and
    // that something acts on it, and nothing here does. The clicked item arrives on the event, so
    // unlike an ItemsRepeater there is no Tag to read it out of.
    private void OnRowClicked(object sender, ItemClickEventArgs e) {
      if (e.ClickedItem is ActivityRow row) {
        row.IsExpanded = !row.IsExpanded;
      }
    }

    // On submit, not on every keystroke: the search runs on the server, and a request per character
    // is a request per character. Emptying the box is handled by the view model, which reloads
    // immediately — nobody expects to press Enter to stop filtering.
    private async void OnSearchSubmitted(
        AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) {
      await ViewModel.SearchCommand.ExecuteAsync(parameter: null);
    }

    // The row comes off Tag rather than DataContext: these templates are inside an ItemsRepeater,
    // which does not set DataContext on what it realizes. A handler that read DataContext would
    // match nothing and return silently on every click.
    private async void OnChipClicked(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: ActivityChip chip }) {
        await ViewModel.SelectCategoryCommand.ExecuteAsync(chip);
      }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: ActivityRow row }) {
        ViewModel.CopyEventCommand.Execute(row);
      }
    }

    private void OnSecureClicked(object sender, RoutedEventArgs e) {
      ViewModel.SecureAccountCommand.Execute(parameter: null);
    }
  }
}
