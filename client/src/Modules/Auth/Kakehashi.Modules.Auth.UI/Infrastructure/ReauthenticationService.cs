using System;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.UI.Views;
using Kakehashi.UI.Common.Helpers;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  // Declining the forced re-sign-in exits the application: once authentication is configured it
  // never keeps running unauthenticated.
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

    // Must be called on the UI thread.
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

        // The shell goes away entirely rather than sitting blurred behind the sign-in window: what
        // the glass would show is the previous user's screen, their name in the rail and their
        // data on the page. The correct amount of the last session to leave visible is none.
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
