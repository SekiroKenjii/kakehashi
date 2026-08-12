using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Kakehashi.App.UI {
  // Everything here is either a static helper x:Bind calls, or a handler that reads its row out
  // of Tag and forwards to the view model. Nothing decides anything: what a move means, what
  // counts as unsaved, and what gets posted are all the view model's, which is what lets them be
  // tested without a window.
  //
  // The rows carry Tag="{x:Bind}" rather than relying on DataContext. That is a lesson
  // this screen already paid for once: a handler that read DataContext matched nothing and
  // returned silently on every interaction.
  public sealed partial class NavigationLayoutPage : Page {
    // A field rather than something in the drag package, because a package carries data and this is
    // an object: putting an identifier in and looking it back up would work and would also mean the
    // drop handler could be handed an identifier from another window. The package still gets text,
    // because a drag with an empty package never starts.
    private NavScreenNode? _dragged;

    // The running slide, kept alive so its Completed is not collected away.
    private Storyboard? _previewSlide;

    // Set while the picker is being put in step with the selection, not by somebody using it.
    private bool _syncingHeadingPicker;

    public NavigationLayoutPage(NavigationLayoutViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      ViewModel.PropertyChanged += OnViewModelChanged;
      ViewModel.Preview.CollectionChanged += (_, _) => RebuildPreview();
      Loaded += OnLoaded;
    }

    public NavigationLayoutViewModel ViewModel { get; }

    public static Visibility WhenSet(string value) {
      return value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // x:Bind has no operator for "not".
    public static Visibility WhenAbsent(object? value) {
      return value is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public static double RowOpacity(bool isVisible) {
      return isVisible ? 1.0 : 0.45;
    }

    // An open eye for a screen the pane offers, a struck-through one for a hidden screen.
    public static string EyeGlyph(bool isVisible) {
      return isVisible ? "" : "";
    }

    // Faded rather than disabled: IsEnabled=false takes the button out of the tab order and stops
    // it surfacing a tooltip, which left the sentence below reaching a mouse and nothing else.
    public static double EyeOpacity(bool canHide) {
      return canHide ? 1.0 : 0.4;
    }

    // Named per row so a screen reader knows which screen the eye acts on.
    public static string VisibilityLabel(string title, bool isVisible) {
      return isVisible ? $"{title}: offered in the pane" : $"{title}: hidden from the pane";
    }

    // A screen that refuses to be hidden says why rather than offering a control with no
    // explanation — the alternative is a button somebody clicks twice and then distrusts.
    //
    // The refusal states the rule rather than asserting a role. It read "this is how the pane is
    // managed", which is true of the Navigation screen and false of the others it appears on: Users
    // and Role permissions are refused because they are shown only to accounts holding their
    // permission, which is a different reason wearing the same words.
    public static string VisibilityTip(bool isVisible, bool canHide) {
      if (!canHide) {
        return "This screen is shown only to accounts that hold its permission, so it cannot also "
            + "be hidden by hand. Take the permission away instead.";
      }
      return isVisible ? "Offered in the pane — click to hide" : "Hidden — click to offer it";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    // The heading picker is assigned here rather than two-way bound. A two-way binding on a picker
    // whose item list is rebuilt writes null while the list is being replaced, and "no heading" is
    // a real choice whose id happens to be empty — so a rebuild would read as somebody unfiling the
    // screen. This screen has had that bug before, in the row pickers this replaced.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(NavigationLayoutViewModel.IsPreviewOpen)) {
        SlidePreview(ViewModel.IsPreviewOpen);
        return;
      }
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

    // How far the panel travels, which is its own width: closed means just off the edge.
    private const double _previewTravel = 360;

    // Hand-animated because the panel is always in the tree: it only changes Visibility, and the
    // theme transitions animate an element arriving rather than one becoming visible, so the panel
    // simply appeared. Visibility is still set — collapsed once it has left, so nothing offscreen
    // stays hit-testable, and visible before it starts so there is something to watch move.
    private void SlidePreview(bool open) {
      if (open) {
        PreviewPanel.Visibility = Visibility.Visible;
      }

      var slide = new DoubleAnimation {
        To = open ? 0 : _previewTravel,
        Duration = new Duration(TimeSpan.FromMilliseconds(220)),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
      };
      Storyboard.SetTarget(slide, PreviewSlide);
      Storyboard.SetTargetProperty(slide, "X");

      // A Storyboard nobody references can be collected before it finishes, and the Completed that
      // collapses the panel then never runs - which left the panel sitting open and hit-testable
      // over the page after it had been asked to close.
      _previewSlide?.Stop();
      _previewSlide = new Storyboard();
      _previewSlide.Children.Add(slide);
      if (!open) {
        _previewSlide.Completed += (_, _) => PreviewPanel.Visibility = Visibility.Collapsed;
      }
      _previewSlide.Begin();
    }

    private void OnTogglePreviewClicked(object sender, RoutedEventArgs e) {
      ViewModel.IsPreviewOpen = !ViewModel.IsPreviewOpen;
    }

    private void OnClosePreviewClicked(object sender, RoutedEventArgs e) {
      ViewModel.IsPreviewOpen = false;
    }

    // Built here rather than bound, because NavigationView takes headers and items as different
    // types and the view model must not make controls - it is constructed off the UI thread by its
    // tests. Rebuilt wholesale on every change: the preview is small, and a diffing version would
    // be a second placement algorithm to keep in step with the planner.
    private void RebuildPreview() {
      PanePreview.MenuItems.Clear();

      string group = string.Empty;
      foreach (var entry in ViewModel.Preview) {
        if (!string.Equals(entry.Item.Group, group, StringComparison.Ordinal)) {
          group = entry.Item.Group;
          if (group.Length > 0) {
            PanePreview.MenuItems.Add(new NavigationViewItemHeader { Content = group });
          }
        }

        PanePreview.MenuItems.Add(new NavigationViewItem {
          Content = entry.Item.Title,
          Icon = new FontIcon { Glyph = entry.Item.IconGlyph },
          IsEnabled = entry.IsEnabled,
          SelectsOnInvoked = false,
        });
      }
    }

    private void OnScreenPressed(object sender, RoutedEventArgs e) {
      if (sender is FrameworkElement { Tag: NavScreenNode screen }) {
        ViewModel.SelectedScreen = screen;
      }
    }

    // The row is a Border rather than a Button on purpose — a Button is a leaf in the automation
    // tree, so it would hide the eye it contains — and a Border answers no key on its own.
    private void OnScreenKeyDown(object sender, KeyRoutedEventArgs e) {
      if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) {
        return;
      }
      if (sender is FrameworkElement { Tag: NavScreenNode screen }) {
        ViewModel.SelectedScreen = screen;
        e.Handled = true;
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

    private void OnHeadingDrop(object sender, DragEventArgs e) {
      if (_dragged is { } screen && sender is FrameworkElement { Tag: NavHeadingNode heading }) {
        ViewModel.MoveScreen(screen, heading, heading.Screens.Count);
      }
      _dragged = null;
      e.Handled = true;
    }

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
