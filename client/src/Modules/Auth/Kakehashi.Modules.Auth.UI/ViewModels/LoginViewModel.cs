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
  /// <summary>
  /// Drives the sign-in window, whose shape depends on <see cref="AuthMode"/>.
  /// </summary>
  /// <remarks>
  /// In-app mode is one screen: a credentials form that shows its own errors inline and disables
  /// itself while the attempt is in flight, because that is what every sign-in form does and because
  /// a failed attempt must leave the typed email where the user can correct it.
  /// <para>
  /// Browser mode keeps three: explain the flow, wait for the browser, report the failure. The user
  /// is doing the work in another window, so the app has nothing to show but progress — and when it
  /// fails there is no field to return to, only a retry.
  /// </para>
  /// Either way <see cref="SignInSucceeded"/> fires when the flow completes so the window can close.
  /// </remarks>
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

    /// <summary>
    /// What actually protects the password on its way to the server.
    /// </summary>
    /// <remarks>
    /// Derived from the configured authority's scheme rather than asserted. The line under the
    /// sign-in button used to read "Sent over TLS to your Kakehashi server" no matter what, beside a
    /// green shield — which for the http authority this ships with was a plain untruth, in the one
    /// place a person looks before typing a password.
    /// <para>
    /// Loopback is called out separately because it is neither: nothing leaves the machine, so
    /// warning about interception would be alarming and wrong, and claiming TLS would still be a lie.
    /// </para>
    /// </remarks>
    public string TransportSummary {
      get {
        return DetectTransport() switch {
          Transport.Tls => "Sent over TLS to your Kakehashi server",
          Transport.Loopback => "Sent to a server on this machine — no network involved",
          _ => "Not encrypted: this server is configured over plain HTTP",
        };
      }
    }

    /// <summary>The shield, or the warning triangle when there is nothing to be reassured about.</summary>
    public string TransportGlyph {
      get { return DetectTransport() == Transport.Plain ? "" : ""; }
    }

    /// <summary>Green for TLS, neutral for loopback, caution for plain HTTP over a network.</summary>
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

    /// <summary>Whether the last attempt produced an error worth showing.</summary>
    public bool HasError => !IsBusy && ErrorMessage is not null;

    /// <summary>Whether this build asks for the password itself. See <see cref="AuthMode"/>.</summary>
    public bool IsInAppMode => _options.Mode == AuthMode.InApp;

    /// <summary>The credentials form — the whole of the window in in-app mode.</summary>
    public bool ShowsCredentialForm => IsInAppMode;

    /// <summary>Whether the form accepts input, i.e. no attempt is in flight.</summary>
    public bool IsFormEnabled => !IsBusy;

    /// <summary>Browser mode, step 1: explain what is about to open.</summary>
    public bool ShowsBrowserPrompt => !IsInAppMode && !IsBusy && ErrorMessage is null;

    /// <summary>Browser mode, step 2: the browser is open and the callback has not arrived.</summary>
    public bool ShowsBrowserWaiting => !IsInAppMode && IsBusy;

    /// <summary>Browser mode, step 3: the attempt failed and there is no field to correct.</summary>
    public bool ShowsBrowserError => !IsInAppMode && !IsBusy && ErrorMessage is not null;

    /// <summary>The application version shown in the footer, e.g. <c>v1.0.0</c>.</summary>
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

    /// <summary>Raised when sign-in succeeds so the host can dismiss the login window.</summary>
    public event EventHandler? SignInSucceeded;

    /// <summary>
    /// In-app mode needs both fields before there is anything to send. Browser mode collects nothing
    /// here, so the button is always live.
    /// </summary>
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
        // The user cancelled from the waiting state; fall back to the initial state, not an error.
      } finally {
        IsBusy = false;
      }
    }

    /// <summary>Re-opens the system browser at the in-progress authorize URL.</summary>
    [RelayCommand]
    private void ReopenBrowser() {
      _browser.TryReopen();
    }

    /// <summary>Opens the authorization server in the browser so the user can check reachability.</summary>
    [RelayCommand]
    private void TroubleshootConnection() {
      if (_options.Authority is { Length: > 0 } authority) {
        using var process = Process.Start(
            new ProcessStartInfo { FileName = authority, UseShellExecute = true });
      }
    }
  }
}
