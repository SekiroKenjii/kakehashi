using System;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Modules.Auth.UI;
using Kakehashi.UI.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
  /// <summary>
  /// The default landing page; reloads itself whenever the auth session or the module set changes.
  /// </summary>
  public sealed partial class HomePage : Page {
    public HomePage(HomeViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      Loaded += OnLoaded;
      Unloaded += OnUnloaded;
    }

    public HomeViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      // Subscribe on Loaded, drop on Unloaded, UnregisterAll first:
      // client/docs/architecture.md, "A page subscribes on Loaded".
      WeakReferenceMessenger.Default.UnregisterAll(this);
      WeakReferenceMessenger.Default.Register<HomePage, AuthSessionChangedMessage>(
          this, static (page, message) => page.DispatcherQueue.TryEnqueue(
              () => _ = page.ViewModel.LoadCommand.ExecuteAsync(parameter: null)));
      WeakReferenceMessenger.Default.Register<HomePage, ModuleSetChangedMessage>(
          this, static (page, message) => page.DispatcherQueue.TryEnqueue(
              () => _ = page.ViewModel.LoadCommand.ExecuteAsync(parameter: null)));

      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
      WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void OnExploreModulesClick(object sender, RoutedEventArgs e) {
      ModulesCard.StartBringIntoView();
    }

    private void OnStepClick(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is GettingStartedStep step) {
        ViewModel.OpenStepCommand.Execute(step);
      }
    }

    private void OnModuleCardClick(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is ModuleCardItem module) {
        ViewModel.OpenModuleCommand.Execute(module);
      }
    }

    private async void OnDetachModuleClick(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is not ModuleCardItem { CanDetach: true } module) {
        return;
      }

      ViewModel.PrepareDetach(module);
      var result = await DetachModuleDialog.ShowAsync();
      if (result == ContentDialogResult.Primary) {
        ViewModel.ConfirmDetach();
      }
    }

    private async void OnRegisterModuleClick(object sender, RoutedEventArgs e) {
      ViewModel.PrepareAttachModules();
      await AttachModuleDialog.ShowAsync();
    }

    /// <summary>
    /// Attaches a module without closing the dialog, so several can be re-attached in one visit.
    /// The attach command refreshes the list, so the row leaves and the empty text appears.
    /// </summary>
    private void OnAttachModuleClick(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is DetachedModuleListItem module) {
        ViewModel.AttachModuleCommand.Execute(module);
      }
    }
  }
}
