using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.Modules.Auth.UI.Infrastructure;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml.Media;
using SignInRequest = Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn.SignInCommand;

namespace Kakehashi.Modules.Auth.UI.ViewModels {
  // The window's shape depends on AuthMode. In-app mode is one screen: a credentials form that
  // shows errors inline and keeps the typed email so a failed attempt can be corrected. Browser
  // mode is three exclusive states — explain, wait, report — because the user works in another
  // window and a failure leaves no field to return to, only a retry.
  public sealed partial class LoginViewModel : ViewModel {
    private readonly ISender _sender;
    private readonly SystemBrowser _browser;
    private readonly AuthOptions _options;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsFormEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowsBrowserPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowsBrowserWaiting))]
    [NotifyPropertyChangedFor(nameof(ShowsBrowserError))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowsBrowserPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowsBrowserError))]
    public partial string? ErrorMessage { get; set; }

    // Derived from the configured authority's scheme, never asserted. This line used to read "Sent
    // over TLS to your Kakehashi server" no matter what, beside a green shield — an untruth for
    // the http authority this ships with, in the one place a person looks before typing a
    // password. Loopback is a third case because nothing leaves the machine: warning about
    // interception would be alarming and wrong, and claiming TLS would still be a lie.
    public string TransportSummary {
      get {
        return DetectTransport() switch {
          Transport.Tls => "Sent over TLS to your Kakehashi server",
          Transport.Loopback => "Sent to a server on this machine — no network involved",
          _ => "Not encrypted: this server is configured over plain HTTP",
        };
      }
    }

    public string TransportGlyph {
      get { return DetectTransport() == Transport.Plain ? "" : ""; }
    }

    public Brush TransportBrush {
      get {
        var key = DetectTransport() switch {
          Transport.Tls => "SystemFillColorSuccessBrush",
          Transport.Loopback => "TextFillColorTertiaryBrush",
          _ => "SystemFillColorCautionBrush",
        };
        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
      }
    }

    private enum Transport {
      Plain,
      Loopback,
      Tls,
    }

    private Transport DetectTransport() {
      if (!Uri.TryCreate(_options.Authority, UriKind.Absolute, out var authority)) {
        return Transport.Plain;
      }
      if (authority.Scheme == Uri.UriSchemeHttps) {
        return Transport.Tls;
      }
      return authority.IsLoopback ? Transport.Loopback : Transport.Plain;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Email { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Password { get; set; }

    public bool HasError => !IsBusy && ErrorMessage is not null;

    public bool IsInAppMode => _options.Mode == AuthMode.InApp;

    public bool ShowsCredentialForm => IsInAppMode;

    public bool IsFormEnabled => !IsBusy;

    public bool ShowsBrowserPrompt => !IsInAppMode && !IsBusy && ErrorMessage is null;

    public bool ShowsBrowserWaiting => !IsInAppMode && IsBusy;

    public bool ShowsBrowserError => !IsInAppMode && !IsBusy && ErrorMessage is not null;

    public string VersionText { get; }

    public LoginViewModel(
        ISender sender, SystemBrowser browser, IOptions<AuthOptions> options) {
      ArgumentNullException.ThrowIfNull(sender);
      ArgumentNullException.ThrowIfNull(browser);
      ArgumentNullException.ThrowIfNull(options);
      _sender = sender;
      _browser = browser;
      _options = options.Value;
      Email = string.Empty;
      Password = string.Empty;

      var version = Assembly.GetEntryAssembly()?.GetName().Version;
      VersionText = version is null
          ? string.Empty
          : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public event EventHandler? SignInSucceeded;

    // Browser mode collects nothing here, so its button is always live.
    public bool CanSignIn =>
        !IsBusy
        && (!IsInAppMode
            || (!string.IsNullOrWhiteSpace(Email) && !string.IsNullOrEmpty(Password)));

    [RelayCommand(CanExecute = nameof(CanSignIn), IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken) {
      if (IsBusy) {
        return;
      }

      IsBusy = true;
      ErrorMessage = null;
      try {
        var credentials = IsInAppMode ? new SignInCredentials(Email.Trim(), Password) : null;
        var result = await _sender.Send(new SignInRequest(credentials), cancellationToken);
        if (result.IsSuccess) {
          // The password lives no longer than the attempt that needed it.
          Password = string.Empty;
          SignInSucceeded?.Invoke(this, EventArgs.Empty);
        } else if (result.Error != AuthErrors.LoginCancelled) {
          ErrorMessage = result.Error.Message;
        }
      } catch (OperationCanceledException) {
        // Cancelled from the waiting state: fall back to the initial state, not an error.
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private void ReopenBrowser() {
      _browser.TryReopen();
    }

    [RelayCommand]
    private void TroubleshootConnection() {
      if (_options.Authority is { Length: > 0 } authority) {
        using var process = Process.Start(
            new ProcessStartInfo { FileName = authority, UseShellExecute = true });
      }
    }
  }
}
