using System;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Modules.Auth.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.Modules.Auth.UI.Views {
  // The account page: the signed-in profile, account information, active sessions with revoke,
  // the security activity feed, the session actions, and the edit-profile / change-password
  // dialogs. Reloads itself whenever the auth session changes (e.g. after a re-login).
  public sealed partial class AccountPage : Page {
    public AccountPage(AccountViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      Loaded += OnLoaded;
      Unloaded += OnUnloaded;
    }

    public AccountViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      // Subscribed here and dropped again on Unloaded, not for the life of the object. Pages are
      // transient: navigating away releases this one's WinRT peer while the messenger still holds
      // the managed object, and the next broadcast reads DispatcherQueue off a disposed peer — an
      // ObjectDisposedException that takes the process down. UnregisterAll first, because Register
      // throws on a duplicate and Loaded can fire more than once for the same instance.
      WeakReferenceMessenger.Default.UnregisterAll(this);
      WeakReferenceMessenger.Default.Register<AccountPage, AuthSessionChangedMessage>(
          this, static (page, message) => page.DispatcherQueue.TryEnqueue(
              () => _ = page.ViewModel.LoadCommand.ExecuteAsync(parameter: null)));

      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
      WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private async void OnRevokeSessionClick(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is SessionItem item) {
        await ViewModel.RevokeSessionCommand.ExecuteAsync(item);
      }
    }

    private async void OnEditProfileClick(object sender, RoutedEventArgs e) {
      await ViewModel.PrepareEditProfileAsync();
      await EditProfileDialog.ShowAsync();
    }

    private async void OnChangePasswordClick(object sender, RoutedEventArgs e) {
      ViewModel.PrepareChangePassword();
      await ChangePasswordDialog.ShowAsync();
    }

    // Cancelling the close keeps the dialog open so the validation error stays visible.
    private async void OnEditProfileSave(
        ContentDialog sender, ContentDialogButtonClickEventArgs args) {
      var deferral = args.GetDeferral();
      try {
        args.Cancel = !await ViewModel.SaveProfileAsync();
      } finally {
        deferral.Complete();
      }
    }

    private async void OnChangePasswordSave(
        ContentDialog sender, ContentDialogButtonClickEventArgs args) {
      var deferral = args.GetDeferral();
      try {
        args.Cancel = !await ViewModel.ChangePasswordAsync();
      } finally {
        deferral.Complete();
      }
    }
  }
}
