using System;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.UI.ViewModels;
using Kakehashi.UI.Common.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace Kakehashi.Modules.Auth.UI.Views {
  /// <summary>A small sign-in window shown by the startup gate when interactive login is required.</summary>
  public sealed partial class LoginWindow : WindowEx {
    private readonly TaskCompletionSource<bool> _outcome = new();
    private bool _allowClose;
    private bool _isConfirmingClose;

    public LoginWindow(LoginViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      this.CenterOnScreen();
      WindowHelper.TrySetAppIcon(this);
      ExtendsContentIntoTitleBar = true;

      ViewModel.SignInSucceeded += OnSignInSucceeded;
      AppWindow.Closing += OnClosing;
      Closed += OnClosed;
    }

    public LoginViewModel ViewModel { get; }

    /// <summary>
    /// Completes with <c>true</c> when the user signs in and <c>false</c> when they confirm quitting
    /// at the sign-in prompt. The startup gate awaits this to decide whether to continue or exit.
    /// </summary>
    public Task<bool> Outcome => _outcome.Task;

    private void OnSignInSucceeded(object? sender, EventArgs e) {
      _allowClose = true;
      _outcome.TrySetResult(true);
      Close();
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) {
      if (_allowClose) {
        return;
      }

      // The user is closing the sign-in window without authenticating. Confirm before quitting the
      // app instead of letting an unhandled cancellation surface as the error window.
      args.Cancel = true;

      // A second close request while the dialog is open would make ShowAsync throw.
      if (_isConfirmingClose) {
        return;
      }

      var dialog = new ContentDialog {
        Title = "Quit application?",
        Content = "You need to sign in to use the app. Do you want to quit?",
        PrimaryButtonText = "Quit",
        CloseButtonText = "Back to sign in",
        DefaultButton = ContentDialogButton.Close,
        XamlRoot = Content.XamlRoot,
      };

      _isConfirmingClose = true;
      try {
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) {
          _allowClose = true;
          _outcome.TrySetResult(false);
          Close();
        }
      } finally {
        _isConfirmingClose = false;
      }
    }

    private void OnClosed(object sender, WindowEventArgs args) {
      // Any other close path (no sign-in) is treated as a request to quit.
      _outcome.TrySetResult(false);
    }
  }
}
