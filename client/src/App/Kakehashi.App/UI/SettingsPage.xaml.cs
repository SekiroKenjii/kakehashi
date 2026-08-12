using System;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
  public sealed partial class SettingsPage : Page {
    public SettingsPage(SettingsViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);

      ViewModel = viewModel;

      InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
  }
}
