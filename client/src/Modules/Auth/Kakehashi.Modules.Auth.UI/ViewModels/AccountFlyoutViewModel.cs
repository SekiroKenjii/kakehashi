using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.Application.Abstractions;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions;
using Kakehashi.Modules.Auth.UI.Views;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Xaml;
using Windows.System;
using SignOutRequest = Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut.SignOutCommand;

namespace Kakehashi.Modules.Auth.UI.ViewModels {
  // Sign-out here only sends the use case; the forced re-sign-in is driven by the module's
  // sign-out notification handler.
  public sealed partial class AccountFlyoutViewModel : ViewModel {
    private readonly ISender _sender;
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;
    private readonly IClock _clock;
    private readonly string _supportUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    // Null when unknown, so PersonPicture falls back to its generic person glyph instead of
    // inventing initials.
    [ObservableProperty]
    public partial string? AvatarName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmail))]
    public partial string? Email { get; set; }

    [ObservableProperty]
    public partial string SignedInText { get; set; }

    // Read from the server, never written into the XAML. This line used to be the literal string
    // "2 devices · this + iOS mobile" — a number nobody had, about a device this product has never
    // run on.
    [ObservableProperty]
    public partial string SessionSummary { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public bool HasEmail => !string.IsNullOrEmpty(Email);

    public string StatusText => IsAuthenticated ? "Online" : "Offline";

    public AccountFlyoutViewModel(
        ISender sender,
        INavigationService navigationService,
        IThemeService themeService,
        IClock clock,
        IConfiguration configuration) {
      ArgumentNullException.ThrowIfNull(sender);
      ArgumentNullException.ThrowIfNull(navigationService);
      ArgumentNullException.ThrowIfNull(themeService);
      ArgumentNullException.ThrowIfNull(clock);
      ArgumentNullException.ThrowIfNull(configuration);

      _sender = sender;
      _navigationService = navigationService;
      _themeService = themeService;
      _clock = clock;
      _supportUrl = configuration["Support:Url"] ?? string.Empty;

      DisplayName = "Not signed in";
      SignedInText = "—";
      SessionSummary = "—";
    }

    // Unset hides the row rather than showing it dead: this is a boilerplate with no support site
    // until somebody's product gives it one, and a row that opens nothing teaches a user to stop
    // trusting the menu.
    public bool HasSupport => _supportUrl.Length > 0;

    [RelayCommand]
    private async Task LoadAsync() {
      var session = await _sender.Send(new GetCurrentSessionQuery());
      IsAuthenticated = session.IsAuthenticated;
      DisplayName = session.DisplayName ?? (session.IsAuthenticated ? "Signed in" : "Not signed in");
      AvatarName = session.DisplayName;
      Email = session.Email;
      SignedInText = FormatSignedInAgo(session.SignedInAtUtc);
      SessionSummary = await DescribeSessionsAsync();
      ThemeIndex = _themeService.Theme switch {
        ElementTheme.Light => 1,
        ElementTheme.Dark => 2,
        _ => 0,
      };
    }

    [RelayCommand]
    private void ViewProfile() {
      GoToAccount();
    }

    // Three rows landing on the same page is not a shortcut: password, sessions and audit trail
    // all live there. They stay separate commands because that is what somebody is looking for
    // when they open this menu.
    [RelayCommand]
    private void ChangePassword() {
      GoToAccount();
    }

    [RelayCommand]
    private void ViewSessions() {
      GoToAccount();
    }

    [RelayCommand]
    private void ViewActivity() {
      GoToAccount();
    }

    [RelayCommand]
    private async Task OpenSupportAsync() {
      if (!HasSupport) {
        return;
      }
      await Launcher.LaunchUriAsync(new Uri(_supportUrl));
    }

    private void GoToAccount() {
      _navigationService.NavigateTo(_navigationService.GetPageKey(typeof(AccountPage)));
    }

    private async Task<string> DescribeSessionsAsync() {
      if (!IsAuthenticated) {
        return "—";
      }

      var result = await _sender.Send(new GetRemoteSessionsQuery());
      if (result is null || result.IsFailure) {
        // A count that could not be fetched is left blank rather than shown as zero, which would
        // read as "you are not signed in anywhere" while you plainly are.
        return "—";
      }

      var sessions = result.Value;
      if (sessions.Count == 0) {
        return "no other devices";
      }

      var others = sessions.Count - 1;
      var devices = sessions.Count == 1 ? "1 device" : $"{sessions.Count} devices";
      return others <= 0 ? $"{devices} · this device" : $"{devices} · this + {others} other";
    }

    [RelayCommand]
    private void OpenSettings() {
      // The host registers its settings page under this well-known key; the module deliberately
      // has no reference to the page type itself.
      _navigationService.NavigateTo("Settings");
    }

    [RelayCommand]
    private async Task SignOutAsync() {
      if (!IsAuthenticated) {
        return;
      }

      await _sender.Send(new SignOutRequest());
    }

    partial void OnThemeIndexChanged(int value) {
      _themeService.SetTheme(value switch {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
      });
    }

    private string FormatSignedInAgo(DateTimeOffset? signedInAtUtc) {
      if (signedInAtUtc is not { } signedInAt) {
        return "—";
      }

      var elapsed = _clock.UtcNow - signedInAt;
      if (elapsed < TimeSpan.FromMinutes(1)) {
        return "just now";
      }
      if (elapsed < TimeSpan.FromHours(1)) {
        return $"{(int)elapsed.TotalMinutes}m ago";
      }
      if (elapsed < TimeSpan.FromHours(24)) {
        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m ago";
      }
      return $"{(int)elapsed.TotalDays}d ago";
    }
  }
}
