using System;
using Kakehashi.Modules.Activity.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.Modules.Activity.UI.Views {
  /// <summary>
  /// The activity page: the signed-in account's feed, gathered server-side from every device.
  /// </summary>
  /// <remarks>
  /// It refreshes when you open it and when you ask, and not on a timer. A poll would keep running
  /// while the window is minimised and while the machine is locked, filling the server's request
  /// log with calls nobody is looking at — and this page is not a live monitor. Navigating to it
  /// is the natural "show me now", which is also exactly what the two-machine test does.
  /// </remarks>
  public sealed partial class ActivityPage : Page {
    public ActivityPage(ActivityViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      Loaded += OnLoaded;
    }

    public ActivityViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }
  }
}
