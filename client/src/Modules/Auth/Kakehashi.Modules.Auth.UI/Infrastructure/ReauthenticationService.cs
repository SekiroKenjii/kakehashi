using System;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.UI.Views;
using Kakehashi.UI.Common.Helpers;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  /// <summary>
  /// Forces an interactive re-sign-in after the user signs out: the main window is blurred and its
  /// input disabled while the <see cref="LoginWindow"/> is shown as a modal centered over it. The
  /// user can only sign in again or quit - declining closes the application, so it never keeps
  /// running unauthenticated once authentication is configured.
  /// </summary>
  public sealed class ReauthenticationService {
    private readonly IServiceProvider _services;
    private readonly IShellOverlay _overlay;
    private readonly IMainWindowProvider _mainWindowProvider;
    private readonly AuthOptions _options;
    private bool _inProgress;

    public ReauthenticationService(
        IServiceProvider services,
        IShellOverlay overlay,
        IMainWindowProvider mainWindowProvider,
        IOptions<AuthOptions> options) {
      ArgumentNullException.ThrowIfNull(services);
      ArgumentNullException.ThrowIfNull(overlay);
      ArgumentNullException.ThrowIfNull(mainWindowProvider);
      ArgumentNullException.ThrowIfNull(options);

      _services = services;
      _overlay = overlay;
      _mainWindowProvider = mainWindowProvider;
      _options = options.Value;
    }

    /// <summary>Runs the modal re-sign-in flow. Must be called on the UI thread.</summary>
    public async Task RequireSignInAsync() {
      if (!_options.IsConfigured || _inProgress) {
        return;
      }
      if (_mainWindowProvider.MainWindow is not { } owner) {
        return;
      }

      _inProgress = true;
      try {
        var window = _services.GetRequiredService<LoginWindow>();
        using var overlay = _overlay.Show();
        using var modal = WindowHelper.ShowModalOver(window, owner);

        // The shell goes away entirely, rather than sitting blurred behind the sign-in window.
        // Once somebody has signed out there is no account behind that glass: what it shows is the
        // previous user's screen, with their name in the rail and their data on the page. Blurring
        // it is not enough — the correct amount of the last session to leave visible is none.
        //
        // Hidden after the modal is established, so the ownership and centring above still have a
        // visible owner to work from.
        owner.AppWindow.Hide();

        bool didSignIn = await window.Outcome;
        if (!didSignIn) {
          Microsoft.UI.Xaml.Application.Current.Exit();
          return;
        }
        owner.AppWindow.Show();
      } finally {
        _inProgress = false;
      }
    }
  }
}
