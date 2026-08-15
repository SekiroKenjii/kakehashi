using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Kakehashi.App.Services;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Common.Controls;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI;

/// <summary>One role the selected user holds, with the button that takes it away.</summary>
/// <remarks>
/// The command travels with the row so <c>x:Bind</c> in the item template can reach it. Binding
/// up to the page's view model from inside a <c>DataTemplate</c> means either an ElementName
/// <c>Binding</c> — which is not compile-checked — or a named ancestor this codebase would rather
/// not introduce for one button.
/// </remarks>
public sealed record AssignedRole(
    string Name, bool CanRemove, IAsyncRelayCommand<string> RemoveCommand);

/// <summary>One of the selected user's sessions, with the command that ends it.</summary>
/// <remarks>
/// Same reason the role chip carries its command: an item template cannot reach the page's view
/// model without an ElementName binding, which is not compile-checked.
/// </remarks>
public sealed record SessionEntry(SessionRow Session, IAsyncRelayCommand<string> RevokeCommand);

/// <summary>
/// The Users screen: who exists, what they hold, and the switches an administrator has.
/// </summary>
/// <remarks>
/// The list comes from two server modules — the account module owns people, the authorization
/// module owns what they may do — and is joined by the service beneath this. Neither module holds
/// a copy of the other's fact, which is what stops them disagreeing.
/// <para>
/// Nothing here is enforcement. Every call needs <c>users.manage</c> or <c>roles.manage</c> and
/// the server checks it; this page's own check keeps a screen nobody can use out of the way.
/// </para>
/// </remarks>
public sealed partial class UsersViewModel : ViewModel
{
    /// <summary>The wildcard entries the two filter combos open with.</summary>
    public const string AllRoles = "All roles";
    public const string AllStatus = "All status";

    /// <summary>An account with no sign-in for this long is worth reviewing.</summary>
    private static readonly TimeSpan _idleThreshold = TimeSpan.FromDays(30);

    private readonly IAccessAdminService _admin;
    private readonly IPermissionService _permissions;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IFileSaveService _files;
    private readonly ISender _sender;

    private IReadOnlyList<UserRow> _all = [];
    private IReadOnlyList<SessionRow> _allSessions = [];

    public UsersViewModel(
        IAccessAdminService admin,
        IPermissionService permissions,
        INotificationService notifications,
        IDialogService dialogs,
        IClipboardService clipboard,
        IFileSaveService files,
        ISender sender)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(sender);
        _admin = admin;
        _permissions = permissions;
        _notifications = notifications;
        _dialogs = dialogs;
        ArgumentNullException.ThrowIfNull(files);
        _clipboard = clipboard;
        _files = files;
        _sender = sender;
    }

    /// <summary>Whether the account may use this screen at all.</summary>
    public bool IsPermitted => _permissions.Allows(PermissionKeys.ManageUsers);

    /// <summary>Whether roles can be changed from here — a second permission: listing who holds
    /// what is part of managing users, deciding it is not.</summary>
    public bool CanManageRoles => _permissions.Allows(PermissionKeys.ManageRoles);

    public ObservableCollection<UserRow> Users { get; } = [];

    public ObservableCollection<RoleRow> Roles { get; } = [];

    /// <summary>The selected user's roles, each with its own remove button.</summary>
    public ObservableCollection<AssignedRole> AssignedRoles { get; } = [];

    /// <summary>
    /// The newest few of the selected user's sessions.
    /// </summary>
    /// <remarks>
    /// Capped at three: the full list is every unrevoked session the account has ever opened, which
    /// can run to dozens of near-identical rows. Older ones are counted, not drawn.
    /// </remarks>
    public ObservableCollection<SessionEntry> Sessions { get; } = [];

    /// <summary>The roles the selected user does NOT hold — the only ones worth offering.</summary>
    /// <remarks>
    /// The server accepts a duplicate assignment idempotently, so offering a held role produces a
    /// button that appears to do nothing.
    /// </remarks>
    public ObservableCollection<RoleRow> AssignableRoles { get; } = [];

    /// <summary>The four counts along the top, as the cards that render them.</summary>
    /// <remarks>
    /// Built from the whole set rather than the filtered one: a search must not make the number of
    /// inactive accounts appear to drop.
    /// </remarks>
    public ObservableCollection<StatCard> StatCards { get; } = [];

    /// <summary>The choices in the two filter combos. Roles refresh from the data.</summary>
    public ObservableCollection<string> RoleFilters { get; } = [AllRoles];

    public IReadOnlyList<string> StatusFilters { get; } =
        [AllStatus, "Active", "Inactive", "Never signed in"];

    [ObservableProperty]
    public partial UserRow? SelectedUser { get; set; }

    /// <summary>
    /// Its own flag rather than "is a row selected": the row stays selected after the panel closes,
    /// and a panel derived from the selection cannot close — a ListView with a focused item puts a
    /// cleared selection straight back, reopening it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetailOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignRole))]
    [NotifyCanExecuteChangedFor(nameof(AssignRoleCommand))]
    public partial RoleRow? RoleToAssign { get; set; }

    /// <summary>
    /// Backs the Add button's CanExecute: enabled with an empty picker, the button does nothing and
    /// reports nothing — indistinguishable from a request that failed.
    /// </summary>
    public bool CanAssignRole => RoleToAssign is not null;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RoleFilter { get; set; } = AllRoles;

    [ObservableProperty]
    public partial string StatusFilter { get; set; } = AllStatus;

    [ObservableProperty]
    public partial string CountSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial int ActiveCount { get; set; }

    [ObservableProperty]
    public partial int InactiveCount { get; set; }

    [ObservableProperty]
    public partial int NeverSignedInCount { get; set; }

    [ObservableProperty]
    public partial int IdleCount { get; set; }

    /// <summary>"+4 more" under the session list, or empty when everything is shown.</summary>
    [ObservableProperty]
    public partial string MoreSessions { get; set; } = string.Empty;

    /// <summary>How many sessions the account has in total, for the section heading.</summary>
    [ObservableProperty]
    public partial int SessionCount { get; set; }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!IsPermitted)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var users = await _admin.ListUsersAsync(cancellationToken);
            if (users.IsFailure)
            {
                Notify(users.Error);
                return;
            }
            _all = users.Value;

            var now = DateTimeOffset.Now;
            TotalCount = _all.Count;
            ActiveCount = _all.Count(user => user.IsActive);
            InactiveCount = _all.Count(user => !user.IsActive);
            NeverSignedInCount = _all.Count(user => user.LastSignInAt is null);
            IdleCount = _all.Count(user =>
                user.LastSignInAt is { } at && now - at > _idleThreshold);
            var createdThisMonth = _all.Count(user =>
                user.CreatedAt.Year == now.Year && user.CreatedAt.Month == now.Month);
            var inactiveNeverSignedIn = _all.Count(user => !user.IsActive && user.LastSignInAt is null);

            RebuildStatCards(createdThisMonth, inactiveNeverSignedIn);
            RebuildRoleFilters();
            ApplyFilter();

            // Only asked for when it can be acted on. A read that is going to be refused is a 403 in
            // the log and an error bar the user cannot do anything about.
            if (CanManageRoles && Roles.Count == 0)
            {
                var roles = await _admin.ListRolesAsync(cancellationToken);
                if (roles.IsSuccess)
                {
                    foreach (var role in roles.Value)
                    {
                        Roles.Add(role);
                    }
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Creates an account, with a password the administrator hands over.</summary>
    /// <remarks>
    /// The password is set rather than mailed because this server has no mail; the dialog says so.
    /// </remarks>
    [RelayCommand]
    private async Task AddUserAsync(CancellationToken cancellationToken)
    {
        var values = await _dialogs.ShowInputsAsync(
            "Add user", "Create",
            ("Email", string.Empty, false),
            ("Display name", string.Empty, false),
            ("Temporary password (at least 12 characters)", string.Empty, true));
        if (values is null || string.IsNullOrWhiteSpace(values[0]))
        {
            return;
        }

        var result = await _admin.CreateUserAsync(
            values[0], values[1], values[2], cancellationToken);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }

        _notifications.Show($"{values[0]} created.", InfoBarSeverity.Success);
        await LoadAsync(cancellationToken);
        SelectedUser = Users.FirstOrDefault(user => user.Id == result.Value.Id);
    }

    /// <summary>Writes the list to a CSV where the user asks for it.</summary>
    /// <remarks>
    /// Exports what is on screen — the filtered set, not the whole list. Write failures are
    /// reported: a read-only location or a full disk must not fail silently.
    /// </remarks>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Users.Count == 0)
        {
            _notifications.Show("There is nothing to export.", InfoBarSeverity.Informational);
            return;
        }

        var path = await _files.PickSaveLocationAsync(
            $"users-{DateTime.Now:yyyyMMdd-HHmmss}.csv", "CSV file", ".csv");
        if (path is null)
        {
            return;
        }

        var csv = new StringBuilder("Name,Email,Roles,Status,Last sign-in,Created\n");
        foreach (var user in Users)
        {
            csv.Append(Csv(user.DisplayName)).Append(',')
                .Append(Csv(user.Email)).Append(',')
                .Append(Csv(string.Join("; ", user.RoleNames))).Append(',')
                .Append(user.IsActive ? "Active" : "Inactive").Append(',')
                .Append(user.LastSignInAt?.ToString("u") ?? "never").Append(',')
                .Append(user.CreatedAt.ToString("u")).Append('\n');
        }

        try
        {
            await File.WriteAllTextAsync(path, csv.ToString());
        }
        catch (IOException exception)
        {
            _notifications.Show($"Could not write the file: {exception.Message}", InfoBarSeverity.Error);
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            _notifications.Show($"Could not write the file: {exception.Message}", InfoBarSeverity.Error);
            return;
        }

        _notifications.Show($"Exported {Users.Count} users to {path}", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is not { } user)
        {
            return;
        }

        var values = await _dialogs.ShowInputsAsync(
            $"Edit {user.DisplayName}", "Save",
            ("Display name", user.DisplayName, false),
            ("Phone", user.Phone, false),
            ("Team", user.TeamId, false));
        if (values is null)
        {
            return;
        }

        var result = await _admin.UpdateUserAsync(
            user.Id, values[0], values[1], values[2], cancellationToken);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }

        _notifications.Show("Profile updated.", InfoBarSeverity.Success);
        await ReloadKeepingSelectionAsync(cancellationToken);
    }

    /// <summary>Sets a new password and signs the account out everywhere.</summary>
    [RelayCommand]
    private async Task ResetPasswordAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is not { } user)
        {
            return;
        }

        var values = await _dialogs.ShowInputsAsync(
            $"Reset password for {user.DisplayName}", "Reset",
            ("New password (at least 12 characters)", string.Empty, true));

        // The dialog warns before the call: this server has no mail, so nothing notifies the person,
        // and the reset ends every session they have immediately.
        if (values is null || string.IsNullOrWhiteSpace(values[0]))
        {
            return;
        }

        var result = await _admin.ResetPasswordAsync(user.Id, values[0], cancellationToken);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }

        _notifications.Show(
            $"Password reset. {user.DisplayName} has been signed out everywhere.",
            InfoBarSeverity.Success);
        await ReloadKeepingSelectionAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RevokeSessionAsync(string sessionId)
    {
        if (SelectedUser is not { } user)
        {
            return;
        }

        var result = await _admin.RevokeSessionAsync(user.Id, sessionId, CancellationToken.None);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }
        await ReloadKeepingSelectionAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task SignOutEverywhereAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is not { } user)
        {
            return;
        }

        // From the server, never the panel's cache: it holds the newest three and is empty until
        // its load completes, so a count from it could revoke nothing and report success.
        var live = await _admin.ListUserSessionsAsync(user.Id, cancellationToken);
        if (live.IsFailure)
        {
            Notify(live.Error);
            return;
        }
        if (live.Value.Count == 0)
        {
            _notifications.Show(
                $"{user.DisplayName} has no active sessions.", InfoBarSeverity.Informational);
            return;
        }

        var confirmed = await _dialogs.ShowConfirmAsync(
            $"Sign {user.DisplayName} out everywhere?",
            $"All {live.Value.Count} session(s) end immediately, including this one if the account "
            + "is yours. They can sign in again.",
            "Sign out", "Cancel");
        if (!confirmed)
        {
            return;
        }

        foreach (var session in live.Value)
        {
            var result = await _admin.RevokeSessionAsync(user.Id, session.Id, cancellationToken);
            if (result.IsFailure)
            {
                Notify(result.Error);
                return;
            }
        }

        // When the account is the caller's own, sign this client out too: the server has already
        // revoked the token, so the shell cannot talk to it.
        var current = await _sender.Send(new GetCurrentSessionQuery());
        if (string.Equals(current.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            await _sender.Send(new SignOutCommand());
            return;
        }

        _notifications.Show(
            live.Value.Count == 1
                ? $"Signed {user.DisplayName} out of 1 session."
                : $"Signed {user.DisplayName} out of {live.Value.Count} sessions.",
            InfoBarSeverity.Success);
        await ReloadKeepingSelectionAsync(cancellationToken);
    }

    [RelayCommand]
    private void CopyEmail()
    {
        if (SelectedUser is not { } user)
        {
            return;
        }
        _clipboard.SetText(user.Email);
        _notifications.Show($"{user.Email} copied.", InfoBarSeverity.Informational);
    }

    [RelayCommand]
    private void CopyAccountId()
    {
        if (SelectedUser is not { } user)
        {
            return;
        }
        _clipboard.SetText(user.Id);
        _notifications.Show("Account ID copied.", InfoBarSeverity.Informational);
    }

    [RelayCommand(CanExecute = nameof(CanAssignRole))]
    private async Task AssignRoleAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is null || RoleToAssign is null)
        {
            return;
        }

        // Captured before the call: both are TwoWay-bound selections the user can change in flight,
        // so reading them after is how a success message names the wrong person, or a null one.
        var email = SelectedUser.Email;
        var role = RoleToAssign;
        RoleToAssign = null;

        var result = await _admin.AssignRoleAsync(email, role.Id, cancellationToken);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }

        _notifications.Show($"{role.Name} assigned to {email}.", InfoBarSeverity.Success);
        await ReloadKeepingSelectionAsync(cancellationToken);
    }

    /// <summary>Takes a role away by name, as the detail panel lists them.</summary>
    [RelayCommand]
    private async Task UnassignRoleAsync(string roleName)
    {
        if (SelectedUser is null)
        {
            return;
        }

        var role = Roles.FirstOrDefault(candidate => candidate.Name == roleName);
        if (role is null)
        {
            return;
        }

        var email = SelectedUser.Email;
        var result = await _admin.UnassignRoleAsync(email, role.Id, CancellationToken.None);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }
        await ReloadKeepingSelectionAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is null)
        {
            return;
        }

        var user = SelectedUser;
        if (user.IsActive)
        {
            var confirmed = await _dialogs.ShowConfirmAsync(
                $"Deactivate {user.DisplayName}?",
                "They will be signed out everywhere and cannot sign in until reactivated. Nothing "
                + "they did is deleted.",
                "Deactivate", "Cancel");
            if (!confirmed)
            {
                return;
            }
        }

        var result = await _admin.SetUserActiveAsync(user.Id, !user.IsActive, cancellationToken);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }
        await ReloadKeepingSelectionAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteUserAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is null)
        {
            return;
        }

        var user = SelectedUser;
        var confirmed = await _dialogs.ShowConfirmAsync(
            $"Delete {user.DisplayName}?",
            $"This permanently removes {user.Email}, their sessions and their history. It cannot "
            + "be undone. Deactivating instead keeps the record.",
            "Delete permanently", "Cancel");
        if (!confirmed)
        {
            return;
        }

        var result = await _admin.DeleteUserAsync(user.Id, cancellationToken);
        if (result.IsFailure)
        {
            Notify(result.Error);
            return;
        }

        SelectedUser = null;
        await LoadAsync(cancellationToken);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnRoleFilterChanged(string value) => ApplyFilter();

    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    /// <summary>Closes the detail panel. The row stays selected.</summary>
    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailOpen = false;
    }

    partial void OnSelectedUserChanged(UserRow? value)
    {
        IsDetailOpen = value is not null;
        AssignedRoles.Clear();
        AssignableRoles.Clear();
        Sessions.Clear();
        _allSessions = [];
        SessionCount = 0;
        MoreSessions = string.Empty;
        RoleToAssign = null;
        if (value is null)
        {
            return;
        }

        foreach (var name in value.RoleNames)
        {
            // Hidden without roles.manage. A Remove that is visible, clickable and then refused is
            // worse than no button at all.
            AssignedRoles.Add(new AssignedRole(name, CanManageRoles, UnassignRoleCommand));
        }
        RebuildAssignableRoles(value);
        _ = LoadSessionsAsync(value.Id);
    }

    private void RebuildAssignableRoles(UserRow user)
    {
        AssignableRoles.Clear();
        foreach (var role in Roles.Where(role => !user.RoleNames.Contains(role.Name)))
        {
            AssignableRoles.Add(role);
        }
    }

    /// <summary>Re-reads the list and puts the selection back on the same person.</summary>
    /// <remarks>
    /// The rows are records, so the reloaded row is a different object and a stale reference
    /// matches nothing; ApplyFilter restores the selection by id.
    /// </remarks>
    private async Task ReloadKeepingSelectionAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    private const int _visibleSessions = 3;

    private async Task LoadSessionsAsync(string accountId)
    {
        var result = await _admin.ListUserSessionsAsync(accountId, CancellationToken.None);
        // The administrator may have moved on while the call was in flight; a reply for somebody
        // who is not the selected user must not be drawn under the person who is.
        if (result.IsFailure || SelectedUser?.Id != accountId)
        {
            return;
        }

        _allSessions = result.Value;
        SessionCount = _allSessions.Count;

        Sessions.Clear();
        foreach (var session in _allSessions.Take(_visibleSessions))
        {
            Sessions.Add(new SessionEntry(session, RevokeSessionCommand));
        }

        var hidden = _allSessions.Count - Sessions.Count;
        MoreSessions = hidden > 0 ? $"+{hidden} older session(s) not shown" : string.Empty;
    }

    private static string Csv(string value)
    {
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private void RebuildStatCards(int createdThisMonth, int inactiveNeverSignedIn)
    {
        var percent = TotalCount == 0 ? 0 : ActiveCount * 100 / TotalCount;

        // Uppercase, because every other section label on this screen is. WinUI has no
        // text-transform, so the case is decided here rather than in the style.
        StatCards.Clear();
        StatCards.Add(new StatCard(
            "TOTAL USERS", TotalCount.ToString(),
            createdThisMonth == 0 ? "All accounts" : $"+{createdThisMonth} this month",
            "", StatKind.Accent));
        StatCards.Add(new StatCard(
            "ACTIVE", ActiveCount.ToString(), $"{percent}% of total", "", StatKind.Positive));
        StatCards.Add(new StatCard(
            // The detail must count within the card's population. NeverSignedInCount also includes
            // active accounts, so it can exceed the inactive total it would sit under.
            "INACTIVE", InactiveCount.ToString(),
            $"{inactiveNeverSignedIn} never signed in", "", StatKind.Muted));
        StatCards.Add(new StatCard(
            "IDLE > 30 DAYS", IdleCount.ToString(), "Review for cleanup", "", StatKind.Warning));
    }

    private void RebuildRoleFilters()
    {
        var wanted = new List<string> { AllRoles };
        wanted.AddRange(_all
            .SelectMany(user => user.RoleNames)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

        if (RoleFilters.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            return;
        }

        RoleFilters.Clear();
        foreach (var name in wanted)
        {
            RoleFilters.Add(name);
        }
        if (!RoleFilters.Contains(RoleFilter))
        {
            RoleFilter = AllRoles;
        }
    }

    private void ApplyFilter()
    {
        var needle = SearchText.Trim();

        // Clearing resets the ListView selection and the TwoWay binding writes that null back, so
        // one keystroke in the search box would close the detail panel. Restored by id instead.
        var selectedId = SelectedUser?.Id;

        Users.Clear();
        foreach (var user in _all)
        {
            if (needle.Length > 0
                && !user.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !user.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (RoleFilter != AllRoles && !user.RoleNames.Contains(RoleFilter))
            {
                continue;
            }
            if (!MatchesStatus(user))
            {
                continue;
            }
            Users.Add(user);
        }

        SelectedUser = Users.FirstOrDefault(user => user.Id == selectedId);

        CountSummary = Users.Count == _all.Count
            ? $"{_all.Count} users"
            : $"{Users.Count} of {_all.Count} shown";
    }

    private bool MatchesStatus(UserRow user)
    {
        return StatusFilter switch {
            "Active" => user.IsActive,
            "Inactive" => !user.IsActive,
            "Never signed in" => user.LastSignInAt is null,
            _ => true,
        };
    }

    /// <summary>
    /// Reports a failure, and re-reads the caller's own permissions when the failure was a refusal.
    /// </summary>
    /// <remarks>
    /// See the same method on the role screen: a refusal mid-session means this client's idea of
    /// what the account may do is stale, and the honest answer is the locked panel rather than an
    /// error bar over a screen that looks like it works.
    /// </remarks>
    private void Notify(Error error)
    {
        _notifications.Show(error.Message, InfoBarSeverity.Error);
        if (error.Code == nameof(StatusCode.PermissionDenied))
        {
            _ = RefreshPermissionsAsync();
        }
    }

    private async Task RefreshPermissionsAsync()
    {
        await _permissions.RefreshAsync(CancellationToken.None);
        OnPropertyChanged(nameof(IsPermitted));
        OnPropertyChanged(nameof(CanManageRoles));
    }
}
