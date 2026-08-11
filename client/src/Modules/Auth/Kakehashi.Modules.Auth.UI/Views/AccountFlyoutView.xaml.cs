using System;
using System.ComponentModel;
using Kakehashi.Modules.Auth.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Kakehashi.Modules.Auth.UI.Views {
  // The content of the account flyout opened from the shell's footer avatar item. Raises
  // CloseRequested when an action is taken so the hosting flyout can dismiss itself.
  public sealed partial class AccountFlyoutView : UserControl {
    public AccountFlyoutView(AccountFlyoutViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public AccountFlyoutViewModel ViewModel { get; }

    // Raised when the user takes an action that should dismiss the flyout.
    public event Action? CloseRequested;

    // Resolves the status color: success when signed in, neutral otherwise.
    public Brush StatusBrush(bool isAuthenticated) {
      string key = isAuthenticated ? "SystemFillColorSuccessBrush" : "SystemFillColorNeutralBrush";
      // Fully qualified because Kakehashi.Modules.Auth.Application shadows the XAML type name.
      return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(ViewModel.ThemeIndex)) {
        UpdateThemeButtons();
      }
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e) {
      CloseRequested?.Invoke();
    }

    private void OnThemeButtonClick(object sender, RoutedEventArgs e) {
      if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out int index)) {
        ViewModel.ThemeIndex = index;
      }

      // Re-assert the checked states; clicking the already-selected button must not untoggle it.
      UpdateThemeButtons();
    }

    private void UpdateThemeButtons() {
      SystemThemeButton.IsChecked = ViewModel.ThemeIndex == 0;
      LightThemeButton.IsChecked = ViewModel.ThemeIndex == 1;
      DarkThemeButton.IsChecked = ViewModel.ThemeIndex == 2;
    }
  }
}
