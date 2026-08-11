using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Account.Commands.ChangeRemotePassword;
using Kakehashi.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile;
using Kakehashi.Modules.Auth.Application.Account.Queries.GetRemoteProfile;
using Kakehashi.Modules.Auth.Application.Sessions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using SignInRequest = Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn.SignInCommand;
using SignOutRequest = Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut.SignOutCommand;

namespace Kakehashi.Modules.Auth.UI.ViewModels {
  // A row in the active sessions list.
  public sealed record SessionItem(string Id, string Title, string Subtitle, bool IsCurrent) {
    public bool IsNotCurrent => !IsCurrent;
  }

  // A row in the security activity feed.
  public sealed record ActivityItem(
      string Title, string Subtitle, string TimeText, string Glyph, bool IsAlert) {
    public bool IsNotAlert => !IsAlert;
  }

  // Presentation logic for the Account page: the signed-in profile, the active sessions and
  // security activity fetched from the authorization server (paged client-side), the session
  // actions, and the edit-profile / change-password dialogs.
  public sealed partial class AccountViewModel : ViewModel {
    private const int _pageSize = 5;

    private readonly ISender _sender;
    private readonly IDialogService _dialogs;
    private List<SessionItem> _allSessions = [];
    private List<ActivityItem> _allActivity = [];
    private int _sessionsPage = 1;
    private int _activityPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    [NotifyPropertyChangedFor(nameof(CanSignOut))]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    public partial string? DisplayName { get; set; }

    [ObservableProperty]
    public partial string? Email { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRole))]
    public partial string? RoleText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    [NotifyPropertyChangedFor(nameof(CanSignOut))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string SessionsHeader { get; set; }

    [ObservableProperty]
    public partial bool HasSessionsPaging { get; set; }

    [ObservableProperty]
    public partial string SessionsPageLabel { get; set; }

    [ObservableProperty]
    public partial bool HasActivityPaging { get; set; }

    [ObservableProperty]
    public partial string ActivityPageLabel { get; set; }

    // Edit-profile / change-password dialog state.
    [ObservableProperty]
    public partial string EditDisplayName { get; set; }

    [ObservableProperty]
    public partial string EditPhone { get; set; }

    [ObservableProperty]
    public partial string CurrentPassword { get; set; }

    [ObservableProperty]
    public partial string NewPassword { get; set; }

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialogError))]
    public partial string? DialogError { get; set; }

    public AccountViewModel(ISender sender, IDialogService dialogs) {
      ArgumentNullException.ThrowIfNull(sender);
      ArgumentNullException.ThrowIfNull(dialogs);
      _sender = sender;
      _dialogs = dialogs;
      SessionsHeader = "ACTIVE SESSIONS";
      SessionsPageLabel = string.Empty;
      ActivityPageLabel = string.Empty;
      EditDisplayName = string.Empty;
      EditPhone = string.Empty;
      CurrentPassword = string.Empty;
      NewPassword = string.Empty;
      ConfirmPassword = string.Empty;
    }

    public ObservableCollection<SessionItem> Sessions { get; } = [];

    public ObservableCollection<ActivityItem> Activity { get; } = [];

    public bool CanSignIn => !IsAuthenticated && !IsBusy;

    public bool CanSignOut => IsAuthenticated && !IsBusy;

    public bool IsSignedOut => !IsAuthenticated;

    public bool HasError => ErrorMessage is not null;

    public bool HasRole => !string.IsNullOrEmpty(RoleText);

    public bool HasDialogError => DialogError is not null;

    public string TimeZoneText => TimeZoneInfo.Local.DisplayName;

    public string AuthMethodText => "OAuth 2.0 · OpenID Connect";

    [RelayCommand]
    private async Task LoadAsync() {
      var session = await _sender.Send(new GetCurrentSessionQuery());
      IsAuthenticated = session.IsAuthenticated;
      DisplayName = session.DisplayName;
      Email = session.Email;
      RoleText = session.Roles.Count > 0 ? Title(session.Roles[0]) : null;
      ErrorMessage = null;
      if (IsAuthenticated) {
        await LoadRemoteAsync();
      } else {
        _allSessions = [];
        _allActivity = [];
        ShowSessionsPage(1);
        ShowActivityPage(1);
        SessionsHeader = "ACTIVE SESSIONS";
      }
    }

    [RelayCommand]
    private async Task SignInAsync() {
      if (IsBusy) {
        return;
      }
      IsBusy = true;
      try {
        await _sender.Send(new SignInRequest());
        await LoadAsync();
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private async Task SignOutAsync() {
      if (IsBusy) {
        return;
      }
      IsBusy = true;
      try {
        await _sender.Send(new SignOutRequest());
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private async Task SignOutEverywhereAsync() {
      // Confirmed, because it ends this session too: the button signs the person pressing it out
      // of the machine they are pressing it on, which is not what "all devices" reads like until it
      // has happened.
      var confirmed = await _dialogs.ShowConfirmAsync(
          "Sign out of all devices?",
          "Every session ends immediately, including this one. You will have to sign in again.",
          "Sign out everywhere", "Cancel");
      if (!confirmed) {
        return;
      }

      if (IsBusy) {
        return;
      }
      IsBusy = true;
      try {
        var revoked = await _sender.Send(new RevokeAllSessionsCommand());
        if (revoked.IsFailure) {
          ErrorMessage = revoked.Error.Message;
          return;
        }
        await _sender.Send(new SignOutRequest());
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private async Task RevokeSessionAsync(SessionItem item) {
      if (item is null || IsBusy) {
        return;
      }
      IsBusy = true;
      try {
        var result = await _sender.Send(new RevokeRemoteSessionCommand(item.Id));
        if (result.IsFailure) {
          ErrorMessage = result.Error.Message;
          return;
        }
        await LoadRemoteAsync();
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private void SessionsPrevPage() {
      ShowSessionsPage(_sessionsPage - 1);
    }

    [RelayCommand]
    private void SessionsNextPage() {
      ShowSessionsPage(_sessionsPage + 1);
    }

    [RelayCommand]
    private void ActivityPrevPage() {
      ShowActivityPage(_activityPage - 1);
    }

    [RelayCommand]
    private void ActivityNextPage() {
      ShowActivityPage(_activityPage + 1);
    }

    // Prefills the edit-profile dialog from the server (best-effort).
    public async Task PrepareEditProfileAsync() {
      DialogError = null;
      EditDisplayName = DisplayName ?? string.Empty;
      EditPhone = string.Empty;
      var profile = await _sender.Send(new GetRemoteProfileQuery());
      if (profile.IsSuccess) {
        EditDisplayName = profile.Value.DisplayName ?? string.Empty;
        EditPhone = profile.Value.Phone ?? string.Empty;
      }
    }

    // Saves the profile dialog. Returns false (and sets the error) to keep it open.
    public async Task<bool> SaveProfileAsync() {
      DialogError = null;
      var result = await _sender.Send(new UpdateRemoteProfileCommand(
          string.IsNullOrWhiteSpace(EditDisplayName) ? null : EditDisplayName.Trim(),
          string.IsNullOrWhiteSpace(EditPhone) ? null : EditPhone.Trim()));
      if (result.IsFailure) {
        DialogError = result.Error.Message;
        return false;
      }
      DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? DisplayName : EditDisplayName.Trim();
      await LoadRemoteAsync();
      return true;
    }

    public void PrepareChangePassword() {
      DialogError = null;
      CurrentPassword = string.Empty;
      NewPassword = string.Empty;
      ConfirmPassword = string.Empty;
    }

    // Submits the password dialog. Returns false (and sets the error) to keep it open.
    public async Task<bool> ChangePasswordAsync() {
      DialogError = null;
      if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword)) {
        DialogError = "Enter your current and new password.";
        return false;
      }
      if (NewPassword != ConfirmPassword) {
        DialogError = "The new password and its confirmation do not match.";
        return false;
      }
      var result = await _sender.Send(new ChangeRemotePasswordCommand(CurrentPassword, NewPassword));
      if (result.IsFailure) {
        DialogError = result.Error.Message;
        return false;
      }
      await LoadRemoteAsync();
      return true;
    }

    private async Task LoadRemoteAsync() {
      var sessions = await _sender.Send(new GetRemoteSessionsQuery());
      if (sessions.IsSuccess) {
        _allSessions = [.. sessions.Value.Select(session => new SessionItem(
            session.Id,
            $"{session.Device} · {session.Client}",
            JoinDetails(session.IpAddress, FormatRelative(session.LastSeenAt)),
            session.IsCurrent))];
      } else {
        _allSessions = [];
        ErrorMessage = sessions.Error.Message;
      }
      SessionsHeader = $"ACTIVE SESSIONS ({_allSessions.Count})";
      ShowSessionsPage(1);

      var activity = await _sender.Send(new GetSecurityActivityQuery(Take: 50));
      if (activity.IsSuccess) {
        _allActivity = [.. activity.Value.Select(ToActivityItem)];
      } else {
        // Said, not swallowed. An empty card is how this screen shows "nothing has ever happened to
        // your account", which is the single most reassuring thing it could say — and exactly the
        // wrong thing to say when the truth is that nobody could ask.
        _allActivity = [];
        ErrorMessage = activity.Error.Message;
      }
      ShowActivityPage(1);
    }

    // Whether each pager button has anywhere to go.
    //
    // The buttons were never disabled, so at the first page "previous" was a live control that did
    // nothing — which reads as a broken button rather than as the end of the list.
    [ObservableProperty]
    public partial bool CanPageSessionsBack { get; set; }

    [ObservableProperty]
    public partial bool CanPageSessionsForward { get; set; }

    [ObservableProperty]
    public partial bool CanPageActivityBack { get; set; }

    [ObservableProperty]
    public partial bool CanPageActivityForward { get; set; }

    private void ShowSessionsPage(int page) {
      var pageCount = Math.Max(1, (int)Math.Ceiling(_allSessions.Count / (double)_pageSize));
      _sessionsPage = Math.Clamp(page, 1, pageCount);
      Sessions.Clear();
      foreach (var item in _allSessions.Skip((_sessionsPage - 1) * _pageSize).Take(_pageSize)) {
        Sessions.Add(item);
      }
      HasSessionsPaging = _allSessions.Count > _pageSize;
      SessionsPageLabel = $"{_sessionsPage} / {pageCount}";
      CanPageSessionsBack = _sessionsPage > 1;
      CanPageSessionsForward = _sessionsPage < pageCount;
    }

    private void ShowActivityPage(int page) {
      var pageCount = Math.Max(1, (int)Math.Ceiling(_allActivity.Count / (double)_pageSize));
      _activityPage = Math.Clamp(page, 1, pageCount);
      Activity.Clear();
      foreach (var item in _allActivity.Skip((_activityPage - 1) * _pageSize).Take(_pageSize)) {
        Activity.Add(item);
      }
      HasActivityPaging = _allActivity.Count > _pageSize;
      ActivityPageLabel = $"{_activityPage} / {pageCount}";
      CanPageActivityBack = _activityPage > 1;
      CanPageActivityForward = _activityPage < pageCount;
    }

    private static ActivityItem ToActivityItem(SecurityEventDto entry) {
      var (title, glyph, isAlert) = entry.Kind switch {
        "SignedIn" => ("Signed in", "", false),
        "SignedOut" => ("Signed out", "", false),
        "NewDeviceSignedIn" => ("New device signed in", "", false),
        "PasswordChanged" => ("Password changed", "", false),
        "FailedSignIn" => ("Failed sign-in attempt", "", true),
        "SessionRevoked" => ("Session revoked", "", false),
        _ => (entry.Kind, "", false),
      };
      return new ActivityItem(
          title,
          JoinDetails(entry.Device, entry.IpAddress),
          FormatRelative(entry.OccurredAt),
          glyph,
          isAlert);
    }

    private static string JoinDetails(params string?[] parts) {
      return string.Join(" · ", Array.FindAll(parts, part => !string.IsNullOrEmpty(part)));
    }

    private static string Title(string value) {
      return string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string FormatRelative(DateTimeOffset at) {
      var span = DateTimeOffset.UtcNow - at;
      if (span < TimeSpan.FromMinutes(1)) {
        return "now";
      }
      if (span < TimeSpan.FromHours(1)) {
        return $"{(int)span.TotalMinutes}m ago";
      }
      if (span < TimeSpan.FromDays(1)) {
        return $"{(int)span.TotalHours}h ago";
      }
      if (span < TimeSpan.FromDays(30)) {
        return $"{(int)span.TotalDays}d ago";
      }
      return at.ToLocalTime().ToString("MMM d, yyyy");
    }
  }
}
