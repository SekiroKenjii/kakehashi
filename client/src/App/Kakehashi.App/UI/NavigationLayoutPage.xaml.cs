using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
  /// <summary>
  /// The Navigation screen: where each of this product's destinations sits in the pane.
  /// </summary>
  /// <remarks>
  /// Loads on <see cref="FrameworkElement.Loaded"/>, not <c>OnNavigatedTo</c>: the navigation service
  /// sets <c>Frame.Content</c> directly — pages come from the container, not from
  /// <c>Frame.Navigate</c> — so the navigation overrides never fire. Every page in this app loads the
  /// same way.
  /// <para>
  /// One event handler rather than a command, because a <c>ToggleSwitch</c> reports a change through
  /// an event. It starts by asking the row whether anything actually changed, because the event also
  /// fires while the binding settles and while the repeater recycles the row onto a different
  /// destination.
  /// </para>
  /// <para>
  /// It reads its row from <c>Tag</c>, which the template fills with <c>{x:Bind}</c>, and not from
  /// <c>DataContext</c>. <see cref="ItemsRepeater"/> does not set <c>DataContext</c> on the elements it
  /// realises — that is part of what makes it lighter than a <c>ListView</c> — so a handler that read
  /// it matched nothing and returned, silently, on every interaction. The controls looked like they
  /// worked because their bindings did.
  /// </para>
  /// <para>
  /// The heading picker used to be a second handler here and is now a property on the row, for a
  /// reason worth keeping in mind before adding a third: on recycling, the repeater applies the value
  /// binding before the <c>Tag</c> binding, so the handler ran with the new value and the PREVIOUS
  /// row — and moved a destination nobody had touched.
  /// </para>
  /// </remarks>
  public sealed partial class NavigationLayoutPage : Page {
    public NavigationLayoutPage(NavigationLayoutViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();
      Loaded += OnLoaded;
    }

    public NavigationLayoutViewModel ViewModel { get; }

    /// <summary>Shown only once a heading is selected — there is nothing to edit otherwise.</summary>
    public static Visibility WhenPresent(object? value) {
      return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The inverse of a flag.
    /// </summary>
    /// <remarks>
    /// x:Bind has no operator for "not", and a second property on the view model for every negated
    /// one is worse than one function here.
    /// </remarks>
    public static bool Not(bool value) {
      return !value;
    }

    /// <summary>
    /// The placeholder in the icon box: the name the code uses, so the vocabulary is discoverable.
    /// </summary>
    /// <remarks>
    /// A person cannot be expected to guess that "people" is a name this client knows and "users" is
    /// not, and the field would otherwise be a text box with no clue what belongs in it.
    /// </remarks>
    public static string IconHint(string defaultIcon) {
      return defaultIcon.Length == 0 ? "icon name" : defaultIcon;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
      _ = ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    private void OnVisibilityToggled(object sender, RoutedEventArgs e) {
      if (sender is not ToggleSwitch { Tag: NavDestinationViewModel row }) {
        return;
      }

      if (!row.WouldChangeVisibility()) {
        return;
      }

      row.ApplyCommand.Execute(parameter: null);
    }
  }
}
