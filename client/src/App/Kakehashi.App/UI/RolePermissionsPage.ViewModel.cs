using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Kakehashi.App.Services;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
  /// <summary>One permission as the grid draws it, with the edit staged on top of it.</summary>
  /// <remarks>
  /// The staged value and the saved one both live here: a row that only knew its current value
  /// could not mark itself as changed before the save.
  /// </remarks>
  public sealed partial class GrantViewModel : ObservableObject {
    /// <summary>What a permission is granted at when nothing narrower was chosen.</summary>
    /// <remarks>
    /// The widest, not the narrowest: a permission granted at "own" looks granted while doing
    /// almost nothing, and that failure surfaces off this screen. Narrowing is one deliberate
    /// click.
    /// </remarks>
    public const string DefaultScope = "all";

    private readonly bool _savedEnabled;
    private readonly string _savedScope;

    public GrantViewModel(PermissionRow permission, string savedScope) {
      ArgumentNullException.ThrowIfNull(permission);
      Permission = permission;

      _savedEnabled = savedScope.Length > 0;
      // A grant that is off still carries a scope, so the selector always matches one of its items:
      // its TwoWay binding cannot coerce back to null, and switching one on cannot re-scope another.
      _savedScope = _savedEnabled ? savedScope : DefaultScope;

      IsEnabled = _savedEnabled;
      Scope = _savedScope;
    }

    public PermissionRow Permission { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>The staged scope. Always one of own/team/all, whether granted or not.</summary>
    [ObservableProperty]
    public partial string Scope { get; set; }

    public string Key => Permission.Key;

    public string Name => Permission.Name;

    public string Description => Permission.Description;

    public bool IsHighRisk => Permission.IsHighRisk;

    public bool IsOrdinaryRisk => !Permission.IsHighRisk;

    /// <summary>
    /// Whether to offer the own/team/all picker for this permission.
    /// </summary>
    /// <remarks>
    /// Only where the server says some store narrows on it. Offering the picker on every permission
    /// would make a row-level promise on keys nothing narrows — a choice stored and displayed back
    /// while changing no answer anywhere.
    /// </remarks>
    public bool IsScoped => Permission.IsScoped;

    /// <summary>
    /// The scope only counts while the permission is on: re-scoping a grant nobody holds changes
    /// nothing that would be saved, so it must not count as a pending change.
    /// </summary>
    public bool IsChanged => IsEnabled != _savedEnabled
        || (IsEnabled && !string.Equals(Scope, _savedScope, StringComparison.Ordinal));

    public void Discard() {
      IsEnabled = _savedEnabled;
      Scope = _savedScope;
    }

    partial void OnIsEnabledChanged(bool value) {
      OnPropertyChanged(nameof(IsChanged));
    }

    partial void OnScopeChanged(string value) {
      OnPropertyChanged(nameof(IsChanged));
    }
  }

  /// <summary>A category of permissions, which is how the grid groups them.</summary>
  /// <remarks>
  /// Two lists on purpose. <see cref="All"/> is the category — the count pill and the bulk buttons
  /// act on it. <see cref="Visible"/> is what the current search and filter let through. Filtering
  /// must never re-create the grant rows: they carry the staged edits.
  /// </remarks>
  public sealed partial class PermissionGroupViewModel : ObservableObject {
    public PermissionGroupViewModel(string name, IReadOnlyList<GrantViewModel> grants) {
      Name = name;
      All = grants;
      Visible = [.. grants];
      foreach (var grant in All) {
        grant.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
      }
    }

    public string Name { get; }

    public IReadOnlyList<GrantViewModel> All { get; }

    public ObservableCollection<GrantViewModel> Visible { get; }

    /// <summary>"3 / 4" — how many of this whole category the role holds.</summary>
    public string Summary => $"{All.Count(g => g.IsEnabled)} / {All.Count}";

    /// <summary>Narrows what is shown without touching what is staged.</summary>
    public void ApplyFilter(Func<GrantViewModel, bool> keep) {
      Visible.Clear();
      foreach (var grant in All.Where(keep)) {
        Visible.Add(grant);
      }
    }

    [RelayCommand]
    private void EnableAll() {
      foreach (var grant in All) {
        grant.IsEnabled = true;
      }
    }

    [RelayCommand]
    private void DisableAll() {
      foreach (var grant in All) {
        grant.IsEnabled = false;
      }
    }
  }

  /// <summary>
  /// The Role Permissions screen: pick a role, stage its grants, save once.
  /// </summary>
  /// <remarks>
  /// Grants are staged in this view model and saved in one atomic call:
  /// docs/adr/0004-staged-edits-atomic-apply.md
  /// <para>
  /// Nothing here is enforcement. Every call it makes needs <c>roles.manage</c> and the server
  /// checks it; the page's own check keeps a screen nobody can use out of the way.
  /// </para>
  /// </remarks>
  public sealed partial class RolePermissionsViewModel : ViewModel {
    private readonly IAccessAdminService _admin;
    private readonly IPermissionService _permissions;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;

    private IReadOnlyList<PermissionRow> _catalogue = [];
    private List<PermissionGroupViewModel> _allGroups = [];
    private bool _auditLoading;

    public RolePermissionsViewModel(
        IAccessAdminService admin,
        IPermissionService permissions,
        INotificationService notifications,
        IDialogService dialogs) {
      ArgumentNullException.ThrowIfNull(admin);
      ArgumentNullException.ThrowIfNull(permissions);
      ArgumentNullException.ThrowIfNull(notifications);
      ArgumentNullException.ThrowIfNull(dialogs);
      _admin = admin;
      _permissions = permissions;
      _notifications = notifications;
      _dialogs = dialogs;
    }

    /// <summary>Whether the account may use this screen at all.</summary>
    public bool IsPermitted => _permissions.Allows(PermissionKeys.ManageRoles);

    /// <summary>Whether the audit log is readable. A second permission, checked separately.</summary>
    public bool CanViewAudit => _permissions.Allows(PermissionKeys.ViewAudit);

    public ObservableCollection<RoleRow> Roles { get; } = [];

    /// <summary>The groups the grid draws — only those the current filter leaves something in.</summary>
    public ObservableCollection<PermissionGroupViewModel> Groups { get; } = [];

    public ObservableCollection<AuditRow> AuditEntries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedRole))]
    public partial RoleRow? SelectedRole { get; set; }

    /// <summary>
    /// The server refuses to delete a role the product ships; hiding the menu item keeps a
    /// permanent-deletion confirmation from being offered for an operation that always fails.
    /// </summary>
    public bool CanDeleteSelectedRole => SelectedRole is { IsSystem: false };

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int ChangedCount { get; set; }

    [ObservableProperty]
    public partial string GrantSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangeSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>"All", "Enabled", "Disabled" or "Changed" — the chip row's selection.</summary>
    [ObservableProperty]
    public partial string StateFilter { get; set; } = "All";

    [ObservableProperty]
    public partial bool IsAuditOpen { get; set; }

    /// <summary>Whether there is anything to save. Drives the unsaved-changes bar.</summary>
    public bool HasChanges => ChangedCount > 0;

    /// <summary>Loads the roles and the catalogue, then the first role's grants.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken) {
      if (!IsPermitted) {
        return;
      }

      IsBusy = true;
      try {
        var keepId = SelectedRole?.Id;

        var roles = await _admin.ListRolesAsync(cancellationToken);
        if (roles.IsFailure) {
          Notify(roles.Error);
          return;
        }

        var catalogue = await _admin.ListPermissionsAsync(cancellationToken);
        if (catalogue.IsFailure) {
          Notify(catalogue.Error);
          return;
        }
        _catalogue = catalogue.Value;

        Roles.Clear();
        foreach (var role in roles.Value) {
          Roles.Add(role);
        }

        // Assigning the property runs the selection handler, which loads the grants. The previous
        // selection is kept by id — the rows are records, so the reloaded one is a new object.
        SelectedRole = Roles.FirstOrDefault(role => role.Id == keepId) ?? Roles.FirstOrDefault();
      } finally {
        IsBusy = false;
      }
    }

    /// <summary>Sends the whole staged set and reports what changed.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken) {
      if (SelectedRole is null || !HasChanges) {
        return;
      }

      // Collected from the FULL set, never the filtered view: a grant the current search hides is
      // still a grant, and omitting it from the payload would silently revoke it.
      var wanted = _allGroups
          .SelectMany(group => group.All)
          .Where(grant => grant.IsEnabled)
          .Select(grant => new GrantRow(grant.Key, grant.Scope))
          .ToList();

      IsBusy = true;
      try {
        var result = await _admin.SaveGrantsAsync(SelectedRole.Id, wanted, cancellationToken);
        if (result.IsFailure) {
          Notify(result.Error);
          return;
        }

        var outcome = result.Value;
        _notifications.Show(
            $"Saved: {outcome.Granted} granted, {outcome.Rescoped} re-scoped, "
            + $"{outcome.Revoked} revoked.",
            InfoBarSeverity.Success);

        // Reloaded, not assumed: the role list counts moved, and somebody else may have saved
        // between this screen reading and writing.
        await LoadAsync(cancellationToken);
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private void Discard() {
      foreach (var grant in _allGroups.SelectMany(group => group.All)) {
        grant.Discard();
      }
      RecountChanges();
    }

    [RelayCommand]
    private async Task CloneAsync(CancellationToken cancellationToken) {
      if (SelectedRole is null) {
        return;
      }

      var name = await _dialogs.ShowPromptAsync(
          "Clone role", $"Name for the copy of {SelectedRole.Name}", $"{SelectedRole.Name} copy");
      if (string.IsNullOrWhiteSpace(name)) {
        return;
      }

      var result = await _admin.CreateRoleAsync(
          name, SelectedRole.Description, SelectedRole.Id, cancellationToken);
      if (result.IsFailure) {
        Notify(result.Error);
        return;
      }

      await SelectAfterReloadAsync(result.Value.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task CreateAsync(CancellationToken cancellationToken) {
      var values = await _dialogs.ShowInputsAsync(
          "New role", "Create",
          ("Name", string.Empty, false), ("Description", string.Empty, false));
      if (values is null || string.IsNullOrWhiteSpace(values[0])) {
        return;
      }

      var result = await _admin.CreateRoleAsync(values[0], values[1], string.Empty,
          cancellationToken);
      if (result.IsFailure) {
        Notify(result.Error);
        return;
      }

      await SelectAfterReloadAsync(result.Value.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task EditDetailsAsync(CancellationToken cancellationToken) {
      if (SelectedRole is null) {
        return;
      }

      var values = await _dialogs.ShowInputsAsync(
          "Edit details", "Save",
          ("Name", SelectedRole.Name, false), ("Description", SelectedRole.Description, false));
      if (values is null) {
        return;
      }

      var result = await _admin.UpdateRoleAsync(
          SelectedRole.Id, values[0], values[1], cancellationToken);
      if (result.IsFailure) {
        Notify(result.Error);
        return;
      }

      await SelectAfterReloadAsync(result.Value.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken) {
      if (SelectedRole is null) {
        return;
      }

      var confirmed = await _dialogs.ShowConfirmAsync(
          $"Delete {SelectedRole.Name}?",
          $"{SelectedRole.AccountCount} account(s) hold this role and will lose everything it "
          + "grants. This cannot be undone.",
          "Delete", "Cancel");
      if (!confirmed) {
        return;
      }

      var result = await _admin.DeleteRoleAsync(SelectedRole.Id, cancellationToken);
      if (result.IsFailure) {
        Notify(result.Error);
        return;
      }

      SelectedRole = null;
      await LoadAsync(cancellationToken);
    }

    /// <summary>Opens or closes the audit panel, loading it the first time.</summary>
    /// <remarks>
    /// The in-flight guard is required: the list count stays zero for the whole fetch, so a close
    /// and reopen during it would start a second fetch, and both replies would append every entry
    /// twice.
    /// </remarks>
    [RelayCommand]
    private async Task ToggleAuditAsync(CancellationToken cancellationToken) {
      IsAuditOpen = !IsAuditOpen;
      if (!IsAuditOpen || !CanViewAudit || AuditEntries.Count > 0 || _auditLoading) {
        return;
      }

      _auditLoading = true;
      try {
        var result = await _admin.ListAuditAsync(50, cancellationToken);
        if (result.IsFailure) {
          Notify(result.Error);
          return;
        }
        if (!IsAuditOpen) {
          // Closed while the reply was in flight. Dropping it costs one refetch on the next open
          // and avoids filling a panel nobody is looking at.
          return;
        }
        foreach (var entry in result.Value) {
          AuditEntries.Add(entry);
        }
      } finally {
        _auditLoading = false;
      }
    }

    partial void OnSearchTextChanged(string value) {
      ApplyPermissionFilter();
    }

    partial void OnStateFilterChanged(string value) {
      ApplyPermissionFilter();
    }

    /// <summary>Rebuilds the grid for whichever role is now selected.</summary>
    /// <remarks>
    /// Staged edits on the previous role are dropped without asking — the alternative is a
    /// confirmation between every click on the role list; the unsaved-changes bar is the warning.
    /// </remarks>
    async partial void OnSelectedRoleChanged(RoleRow? value) {
      _allGroups = [];
      Groups.Clear();
      ChangedCount = 0;
      GrantSummary = string.Empty;
      ChangeSummary = string.Empty;
      if (value is null) {
        return;
      }

      var grants = await _admin.ListGrantsAsync(value.Id, CancellationToken.None);
      if (grants.IsFailure) {
        Notify(grants.Error);
        return;
      }
      if (SelectedRole?.Id != value.Id) {
        // Somebody clicked on while this reply was in flight; theirs is the one being drawn.
        return;
      }

      var byKey = grants.Value.ToDictionary(
          grant => grant.PermissionKey, grant => grant.Scope, StringComparer.Ordinal);

      _allGroups = [.. _catalogue
          .GroupBy(permission => permission.Category)
          .Select(category => new PermissionGroupViewModel(
              category.Key,
              [.. category.Select(permission => {
                var row = new GrantViewModel(
                    permission,
                    byKey.TryGetValue(permission.Key, out var scope) ? scope : string.Empty);
                row.PropertyChanged += (_, _) => RecountChanges();
                return row;
              })]))];

      ApplyPermissionFilter();
      RecountChanges();
    }

    partial void OnChangedCountChanged(int value) {
      OnPropertyChanged(nameof(HasChanges));
    }

    private async Task SelectAfterReloadAsync(string roleId, CancellationToken cancellationToken) {
      SelectedRole = null;
      await LoadAsync(cancellationToken);
      SelectedRole = Roles.FirstOrDefault(role => role.Id == roleId) ?? SelectedRole;
    }

    private void ApplyPermissionFilter() {
      var needle = SearchText.Trim();

      Groups.Clear();
      foreach (var group in _allGroups) {
        group.ApplyFilter(grant => Matches(grant, needle));
        if (group.Visible.Count > 0) {
          Groups.Add(group);
        }
      }
    }

    private bool Matches(GrantViewModel grant, string needle) {
      if (needle.Length > 0
          && !grant.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
          && !grant.Key.Contains(needle, StringComparison.OrdinalIgnoreCase)) {
        return false;
      }

      return StateFilter switch {
        "Enabled" => grant.IsEnabled,
        "Disabled" => !grant.IsEnabled,
        "Changed" => grant.IsChanged,
        _ => true,
      };
    }

    /// <summary>
    /// Reports a failure, and re-reads the caller's own permissions when the failure was a refusal.
    /// </summary>
    /// <remarks>
    /// A refusal mid-session means this client's idea of what the account may do is out of date —
    /// most often because the administrator just edited their own role. Re-reading turns the next
    /// render into the honest "you do not have access" panel instead of a working-looking screen
    /// that answers 403 to everything.
    /// </remarks>
    private void Notify(Error error) {
      _notifications.Show(error.Message, InfoBarSeverity.Error);
      if (error.Code == nameof(StatusCode.PermissionDenied)) {
        _ = RefreshPermissionsAsync();
      }
    }

    private async Task RefreshPermissionsAsync() {
      await _permissions.RefreshAsync(CancellationToken.None);
      OnPropertyChanged(nameof(IsPermitted));
      OnPropertyChanged(nameof(CanViewAudit));
    }

    private void RecountChanges() {
      var all = _allGroups.SelectMany(group => group.All).ToList();
      ChangedCount = all.Count(grant => grant.IsChanged);
      GrantSummary = $"{all.Count(grant => grant.IsEnabled)} of {all.Count} enabled";

      var enabled = all.Count(grant => grant.IsChanged && grant.IsEnabled);
      var disabled = all.Count(grant => grant.IsChanged && !grant.IsEnabled);
      ChangeSummary = SelectedRole is null
          ? string.Empty
          : $"— {enabled} enabled, {disabled} disabled on role \"{SelectedRole.Name}\"";
    }
  }
}
