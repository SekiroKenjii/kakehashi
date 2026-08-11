using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Kakehashi.App.UI {
  /// <summary>
  /// The Navigation screen: the structure on the left, the selected screen on the right.
  /// </summary>
  /// <remarks>
  /// Everything here is either a static helper <c>x:Bind</c> calls, or a handler that reads its row out
  /// of <c>Tag</c> and forwards to the view model. Nothing decides anything: what a move means, what
  /// counts as unsaved, and what gets posted are all the view model's, which is what lets them be
  /// tested without a window.
  /// <para>
  /// The rows carry <c>Tag="{x:Bind}"</c> rather than relying on <c>DataContext</c>. That is a lesson
  /// this screen already paid for once: a handler that read <c>DataContext</c> matched nothing and
  /// returned silently on every interaction.
  /// </para>
  /// </remarks>
  public sealed partial class NavigationLayoutPage : Page {
    /// <summary>
    /// The row being dragged.
    /// </summary>
    /// <remarks>
    /// A field rather than something in the drag package, because a package carries data and this is an
    /// object: putting an identifier in and looking it back up would work and would also mean the drop
    /// handler could be handed an identifier from another window. The package still gets text, because
    /// a drag with an empty package never starts.
    /// </remarks>
    private NavScreenNode? _dragged;

    /// <summary>Set while the picker is being put in step with the selection, not by somebody using it.</summary>
    private bool _syncingHeadingPicker;

    public NavigationLayoutPage(NavigationLayoutViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      ViewModel.PropertyChanged += OnViewModelChanged;
      Loaded += OnLoaded;
    }

    public NavigationLayoutViewModel ViewModel { get; }

    /// <summary>Shown when a string has something in it.</summary>
    public static Visibility WhenSet(string value) {
      return value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shown when there is nothing selected. x:Bind has no operator for "not".</summary>
    public static Visibility WhenAbsent(object? value) {
      return value is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A hidden screen is drawn faded, the way the mockup dims it.</summary>
    public static double RowOpacity(bool isVisible) {
      return isVisible ? 1.0 : 0.45;
    }

    /// <summary>An open eye for a screen the pane offers, a struck-through one for a hidden screen.</summary>
    public static string EyeGlyph(bool isVisible) {
      return isVisible ? "" : "";
    }

    /// <summary>
    /// What the eye button says it will do.
    /// </summary>
    /// <remarks>
    /// A screen that refuses to be hidden says why rather than offering a disabled control with no
    /// explanation — the alternative is a button somebody clicks twice and then distrusts.
    /// </remarks>
    public static string VisibilityTip(bool isVisible, bool canHide) {
      if (!canHide) {
        return "This is how the pane is managed, so it cannot be hidden from it.";
      }
      return isVisible ? "Offered in the pane — click to hide" : "Hidden — click to offer it";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    /// <summary>Puts the heading picker in step with whatever is selected.</summary>
    /// <remarks>
    /// Assigned here rather than two-way bound. A two-way binding on a picker whose item list is
    /// rebuilt writes <c>null</c> while the list is being replaced, and "no heading" is a real choice
    /// whose id happens to be empty — so a rebuild would read as somebody unfiling the screen. This
    /// screen has had that bug before, in the row pickers this replaced.
    /// </remarks>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != nameof(NavigationLayoutViewModel.SelectedScreen)) {
        return;
      }

      _syncingHeadingPicker = true;
      try {
        string wanted = ViewModel.SelectedScreen?.Heading is { IsUnfiled: false } heading
            ? heading.Id
            : string.Empty;

        HeadingPicker.SelectedItem = null;
        foreach (var choice in ViewModel.HeadingChoices) {
          if (choice.Id == wanted) {
            HeadingPicker.SelectedItem = choice;
            break;
          }
        }
      } finally {
        _syncingHeadingPicker = false;
      }
    }

    private void OnHeadingChosen(object sender, SelectionChangedEventArgs e) {
      if (_syncingHeadingPicker
          || HeadingPicker.SelectedItem is not NavHeadingChoice choice
          || ViewModel.SelectedScreen is not { } screen) {
        return;
      }

      var target = choice.Id.Length == 0
          ? FindUnfiled()
          : FindHeading(choice.Id);
      if (target is null || ReferenceEquals(target, screen.Heading)) {
        return;
      }

      ViewModel.MoveScreen(screen, target, target.Screens.Count);
    }

    private void OnScreenPressed(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: NavScreenNode screen }) {
        ViewModel.SelectedScreen = screen;
      }
    }

    private void OnVisibilityClicked(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: NavScreenNode screen } && screen.CanHide) {
        screen.IsVisible = !screen.IsVisible;
      }
    }

    private void OnIconClicked(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: NavIconChoice choice }) {
        ViewModel.PickIconCommand.Execute(choice);
      }
    }

    private void OnMoveUpClicked(object sender, RoutedEventArgs e) {
      ViewModel.MoveUpCommand.Execute(ViewModel.SelectedScreen);
    }

    private void OnMoveDownClicked(object sender, RoutedEventArgs e) {
      ViewModel.MoveDownCommand.Execute(ViewModel.SelectedScreen);
    }

    private void OnHeadingUpClicked(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: NavHeadingNode heading }) {
        ViewModel.MoveHeading(heading, IndexOfHeading(heading) - 1);
      }
    }

    private void OnDeleteHeadingClicked(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: NavHeadingNode heading }) {
        ViewModel.DeleteHeadingCommand.Execute(heading);
      }
    }

    private void OnDeleteOrphanClicked(object sender, RoutedEventArgs e) {
      ViewModel.DeleteOrphanCommand.Execute(ViewModel.SelectedScreen);
    }

    private async void OnViewDiffClicked(object sender, RoutedEventArgs e) {
      ViewModel.PrepareDiff();
      DiffDialog.XamlRoot = XamlRoot;
      await DiffDialog.ShowAsync();
    }

    private void OnScreenDragStarting(UIElement sender, DragStartingEventArgs args) {
      if (sender is FrameworkElement { Tag: NavScreenNode screen }) {
        _dragged = screen;

        // Text so the package is not empty; the drop reads the field. A screen is not a string, and
        // anything that arrived as one came from somewhere this page cannot vouch for.
        args.Data.SetText(screen.DisplayTitle);
        args.Data.RequestedOperation = DataPackageOperation.Move;
      }
    }

    private void OnRowDragOver(object sender, DragEventArgs e) {
      e.AcceptedOperation = _dragged is null
          ? DataPackageOperation.None
          : DataPackageOperation.Move;
      e.Handled = true;
    }

    /// <summary>Dropped on a heading: filed at the end of it.</summary>
    private void OnHeadingDrop(object sender, DragEventArgs e) {
      if (_dragged is { } screen && sender is FrameworkElement { Tag: NavHeadingNode heading }) {
        ViewModel.MoveScreen(screen, heading, heading.Screens.Count);
      }
      _dragged = null;
      e.Handled = true;
    }

    /// <summary>Dropped on a row: placed above it.</summary>
    private void OnScreenDrop(object sender, DragEventArgs e) {
      if (_dragged is { } screen
          && sender is FrameworkElement { Tag: NavScreenNode target }
          && target.Heading is { } heading
          && !ReferenceEquals(screen, target)) {
        ViewModel.MoveScreen(screen, heading, heading.Screens.IndexOf(target));
      }
      _dragged = null;
      e.Handled = true;
    }

    private NavHeadingNode? FindHeading(string id) {
      foreach (var heading in ViewModel.Headings) {
        if (!heading.IsUnfiled && heading.Id == id) {
          return heading;
        }
      }
      return null;
    }

    private NavHeadingNode? FindUnfiled() {
      foreach (var heading in ViewModel.Headings) {
        if (heading.IsUnfiled) {
          return heading;
        }
      }
      return null;
    }

    private int IndexOfHeading(NavHeadingNode heading) {
      return ViewModel.Headings.IndexOf(heading);
    }
  }
}
