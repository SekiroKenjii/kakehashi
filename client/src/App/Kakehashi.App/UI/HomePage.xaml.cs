using System;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Modules.Auth.UI;
using Kakehashi.UI.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
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
      // Subscribed here and dropped again on Unloaded, rather than for the life of the object.
      // Pages are transient: navigating away releases this one's WinRT peer while the messenger
      // still holds the managed object, and the next broadcast then reads DispatcherQueue off a
      // disposed peer — an ObjectDisposedException that takes the process down. It only shows up
      // once something broadcasts while the user is on a different page, which is what the access
      // gate does when an administrator changes an assignment.
      //
      // UnregisterAll first, because Register throws on a duplicate and Loaded can fire more than
      // once for the same instance.
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

    // The dialog stays open so several modules can be re-attached in one visit; the attach command
    // refreshes the list, so the row disappears and the empty text appears when done.
    private void OnAttachModuleClick(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is DetachedModuleListItem module) {
        ViewModel.AttachModuleCommand.Execute(module);
      }
    }
  }
}
