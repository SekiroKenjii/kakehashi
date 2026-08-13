using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Services;
using Kakehashi.App.UI;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts.Services.Platform;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.UI {
  /// <summary>
  /// Unit tests for the two administration screens.
  /// </summary>
  /// <remarks>
  /// What is worth testing here is the staging: the Role Permissions screen holds edits until Save,
  /// and every visible number — the changed count, the per-category summary, the payload that goes
  /// on the wire — is derived from that. None of it involves a control, so none of it needs a UI
  /// thread.
  /// </remarks>
  public sealed class AccessAdminViewModelTests {
    private static readonly RoleRow _admin = new("role-1", "Admin", "Everything", true, 2, 1);
    private static readonly RoleRow _viewer = new("role-2", "Viewer", "Read only", true, 1, 4);

    /// <summary>
    /// The one permission whose row scope a store actually honours, so the only one whose grant
    /// offers own/team/all.
    /// </summary>
    private static readonly PermissionRow _manageUsers =
        new("users.manage", "Manage Users", "…", "Administration", true, IsScoped: true);
    private static readonly PermissionRow _viewAudit =
        new("audit.view", "View Audit Log", "…", "Administration", false, IsScoped: false);
    private static readonly PermissionRow _notesAccess =
        new("notes.access", "Use notes", "…", "Module access", false, IsScoped: false);

    private readonly IAccessAdminService _admins = Substitute.For<IAccessAdminService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly IFileSaveService _files = Substitute.For<IFileSaveService>();
    private readonly ISender _sender = Substitute.For<ISender>();

    public AccessAdminViewModelTests() {
      _permissions.Allows(Arg.Any<string>()).Returns(true);

      _admins.ListRolesAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<RoleRow>>([_admin, _viewer]));
      _admins.ListPermissionsAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<PermissionRow>>(
              [_manageUsers, _viewAudit, _notesAccess]));
      _admins.ListGrantsAsync(_admin.Id, Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<GrantRow>>(
              [new GrantRow("users.manage", "all"), new GrantRow("notes.access", "team")]));
      _admins.ListGrantsAsync(_viewer.Id, Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<GrantRow>>([new GrantRow("notes.access", "own")]));
    }

    private RolePermissionsViewModel CreateRoles() {
      return new RolePermissionsViewModel(_admins, _permissions, _notifications, _dialogs);
    }

    private UsersViewModel CreateUsers() {
      return new UsersViewModel(
          _admins, _permissions, _notifications, _dialogs, _clipboard, _files, _sender);
    }

    [Fact]
    public async Task Load_GroupsTheCatalogueAndMarksWhatTheRoleHolds() {
      var sut = CreateRoles();

      await sut.LoadCommand.ExecuteAsync(null);

      Assert.Equal(2, sut.Roles.Count);
      Assert.Equal(_admin.Id, sut.SelectedRole?.Id);

      // Two categories, in the order the catalogue arrived.
      Assert.Equal(["Administration", "Module access"], sut.Groups.Select(g => g.Name));
      Assert.Equal("1 / 2", sut.Groups[0].Summary);
      Assert.Equal("2 of 3 enabled", sut.GrantSummary);
      Assert.Equal(0, sut.ChangedCount);
      Assert.False(sut.HasChanges);
    }

    [Fact]
    public async Task EnablingAPermission_CountsAsOneChangeAndDefaultsToTheWidestScope() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      var audit = Grant(sut, "audit.view");
      audit.IsEnabled = true;

      Assert.Equal("all", audit.Scope);
      Assert.True(audit.IsChanged);
      Assert.Equal(1, sut.ChangedCount);
      Assert.True(sut.HasChanges);
      Assert.Equal("2 / 2", sut.Groups[0].Summary);
    }

    [Fact]
    public async Task NarrowingAScope_IsAChangeWithoutTurningThePermissionOff() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      var notes = Grant(sut, "notes.access");
      notes.Scope = "own";

      Assert.True(notes.IsEnabled);
      Assert.True(notes.IsChanged);
      Assert.Equal(1, sut.ChangedCount);
    }

    [Fact]
    public async Task Discard_PutsEveryRowBackAndClearsTheBar() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      Grant(sut, "audit.view").IsEnabled = true;
      Grant(sut, "users.manage").IsEnabled = false;
      Assert.Equal(2, sut.ChangedCount);

      sut.DiscardCommand.Execute(null);

      Assert.Equal(0, sut.ChangedCount);
      Assert.False(sut.HasChanges);
      Assert.True(Grant(sut, "users.manage").IsEnabled);
      Assert.False(Grant(sut, "audit.view").IsEnabled);
    }

    [Fact]
    public async Task Save_SendsTheWholeSetSoARevokedPermissionIsSimplyAbsent() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      Grant(sut, "users.manage").IsEnabled = false;
      Grant(sut, "audit.view").IsEnabled = true;

      IReadOnlyCollection<GrantRow>? sent = null;
      _admins.SaveGrantsAsync(_admin.Id, Arg.Any<IReadOnlyCollection<GrantRow>>(),
              Arg.Any<CancellationToken>())
          .Returns(call => {
            sent = call.ArgAt<IReadOnlyCollection<GrantRow>>(1);
            return Task.FromResult(Result.Success(new SaveOutcome(1, 1, 0)));
          });

      await sut.SaveCommand.ExecuteAsync(null);

      Assert.NotNull(sent);
      Assert.Equal(["audit.view", "notes.access"], sent!.Select(g => g.PermissionKey).Order());
      Assert.DoesNotContain(sent, g => g.PermissionKey == "users.manage");
      Assert.Equal("team", sent.Single(g => g.PermissionKey == "notes.access").Scope);
    }

    [Fact]
    public async Task Save_DoesNothingWhenNothingIsStaged() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      await sut.SaveCommand.ExecuteAsync(null);

      await _admins.DidNotReceive().SaveGrantsAsync(
          Arg.Any<string>(), Arg.Any<IReadOnlyCollection<GrantRow>>(),
          Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectingAnotherRole_RebuildsTheGridFromThatRolesGrants() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      sut.SelectedRole = sut.Roles.Single(role => role.Id == _viewer.Id);
      // The selection handler is async void by necessity — the property setter cannot await — so
      // the assertions wait for the one call it makes rather than for a wall-clock delay.
      await _admins.Received().ListGrantsAsync(_viewer.Id, Arg.Any<CancellationToken>());

      Assert.Equal("1 of 3 enabled", sut.GrantSummary);
      Assert.Equal("own", Grant(sut, "notes.access").Scope);
      Assert.False(Grant(sut, "users.manage").IsEnabled);
    }

    [Fact]
    public async Task GroupAllOn_EnablesEveryPermissionInThatCategoryOnly() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      sut.Groups[0].EnableAllCommand.Execute(null);

      Assert.Equal("2 / 2", sut.Groups[0].Summary);
      Assert.Equal("1 / 1", sut.Groups[1].Summary);
    }

    [Fact]
    public async Task WithoutThePermission_TheRolesScreenNeitherLoadsNorRenders() {
      _permissions.Allows(PermissionKeys.ManageRoles).Returns(false);
      var sut = CreateRoles();

      Assert.False(sut.IsPermitted);

      // Awaited, and the "does not load" half asserted. Fire-and-forget meant the command had not
      // finished when the assertion ran, so this passed with the permission guard deleted: an empty
      // collection proves nothing if nothing has had a chance to fill it.
      await sut.LoadCommand.ExecuteAsync(parameter: null);

      Assert.Empty(sut.Roles);
      await _admins.DidNotReceive().ListRolesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Users_InactiveCardCountsNeverSignedInWithinItsOwnPopulation() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            // Active and never signed in: the case that made the card report a detail bigger than
            // the number it sat under. The counts test below cannot catch it — its only
            // never-signed-in account is also its only inactive one.
            new UserRow("1", "new@x.test", "New", "", "", true, null, now, 0, []),
            new UserRow("2", "gone@x.test", "Gone", "", "", false, null, now, 0, []),
          ]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);

      Assert.Equal(2, sut.NeverSignedInCount);
      Assert.Equal(1, sut.InactiveCount);

      var inactive = sut.StatCards.Single(card => card.Label == "INACTIVE");
      Assert.Equal("1", inactive.Value);
      Assert.Equal("1 never signed in", inactive.Detail);
    }

    [Fact]
    public async Task Users_CountsTheWholeSetAndFiltersOnlyTheList() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now.AddMinutes(-5), now, 2, ["Admin"]),
            new UserRow("2", "bob@x.test", "Bob", "", "", true, now.AddDays(-90), now, 0, []),
            new UserRow("3", "cy@x.test", "Cy", "", "", false, null, now, 0, ["Viewer"]),
          ]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);

      Assert.Equal(3, sut.TotalCount);
      Assert.Equal(2, sut.ActiveCount);
      Assert.Equal(1, sut.InactiveCount);
      Assert.Equal(1, sut.NeverSignedInCount);
      Assert.Equal(1, sut.IdleCount);
      Assert.Equal(4, sut.StatCards.Count);

      sut.SearchText = "bob";

      // The list narrows; the counts do not. A search that appeared to change how many accounts
      // are inactive would be a search that lies.
      Assert.Single(sut.Users);
      Assert.Equal("Bob", sut.Users[0].DisplayName);
      Assert.Equal(3, sut.TotalCount);
      Assert.Equal(1, sut.InactiveCount);
    }

    [Fact]
    public async Task SelectingAUser_ListsTheirRolesWithARemoveCommand() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 1, ["Admin", "Viewer"]),
          ]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];

      Assert.Equal(["Admin", "Viewer"], sut.AssignedRoles.Select(role => role.Name));
      Assert.All(sut.AssignedRoles, role => Assert.NotNull(role.RemoveCommand));
    }

    [Fact]
    public async Task DeactivatingAUser_AsksFirstAndDoesNothingIfRefused() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 1, []),
          ]));
      _dialogs.ShowConfirmAsync(
              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
          .Returns(false);

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];

      await sut.ToggleActiveCommand.ExecuteAsync(null);

      await _admins.DidNotReceive().SetUserActiveAsync(
          Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivatingAUser_NeedsNoConfirmation() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", false, now, now, 0, []),
          ]));
      _admins.SetUserActiveAsync("1", true, Arg.Any<CancellationToken>())
          .Returns(Result.Success());

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];

      await sut.ToggleActiveCommand.ExecuteAsync(null);

      await _admins.Received(1).SetUserActiveAsync("1", true, Arg.Any<CancellationToken>());
      await _dialogs.DidNotReceive().ShowConfirmAsync(
          Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // The four regressions the review found. Each was a real defect, so each keeps a test.

    [Fact]
    public async Task AllOn_DoesNotWidenAGrantThatWasAlreadyOnAtANarrowerScope() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      // notes.access is saved at "team". Turning the whole category on must leave it there.
      sut.Groups.Single(group => group.Name == "Module access").EnableAllCommand.Execute(null);

      var notes = Grant(sut, "notes.access");
      Assert.True(notes.IsEnabled);
      Assert.Equal("team", notes.Scope);
      Assert.False(notes.IsChanged);
      Assert.Equal(0, sut.ChangedCount);
    }

    [Fact]
    public async Task TogglingOffAndBackOn_RestoresTheSavedScopeRatherThanWidening() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      var notes = Grant(sut, "notes.access");
      notes.IsEnabled = false;
      Assert.True(notes.IsChanged);

      notes.IsEnabled = true;

      Assert.Equal("team", notes.Scope);
      Assert.False(notes.IsChanged);
    }

    [Fact]
    public async Task ReScopingAGrantThatIsOff_IsNotCountedAsAChange() {
      var sut = CreateRoles();
      await sut.LoadCommand.ExecuteAsync(null);

      // audit.view is not granted; the selector beside it is disabled, but nothing that moves it
      // should put a number on the unsaved-changes bar that Save cannot account for.
      Grant(sut, "audit.view").Scope = "own";

      Assert.Equal(0, sut.ChangedCount);
      Assert.False(sut.HasChanges);
    }

    [Fact]
    public async Task SearchingUsers_KeepsTheSelectedUserAndTheirDetailPanel() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 1, ["Admin"]),
            new UserRow("2", "bob@x.test", "Bob", "", "", true, now, now, 0, []),
          ]));
      _admins.ListUserSessionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users.Single(user => user.Id == "1");

      // A row the search still matches keeps its selection; the detail panel stays open.
      sut.SearchText = "ada";

      Assert.Equal("1", sut.SelectedUser?.Id);
      Assert.Single(sut.AssignedRoles);

      // A search that excludes them clears it, which is the honest answer — the row is gone.
      sut.SearchText = "bob";

      Assert.Null(sut.SelectedUser);
    }

    [Fact]
    public async Task RemoveRoleButton_IsHiddenWithoutRolesManage() {
      _permissions.Allows(PermissionKeys.ManageRoles).Returns(false);
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 1, ["Admin"]),
          ]));
      _admins.ListUserSessionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];

      Assert.False(sut.CanManageRoles);
      Assert.All(sut.AssignedRoles, role => Assert.False(role.CanRemove));
    }

    [Fact]
    public async Task SelectingAUser_LoadsTheirSessions() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 1, []),
          ]));
      _admins.ListUserSessionsAsync("1", Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([
            new SessionRow("s1", "kakehashi-desktop", "Windows", "10.0.0.1", now, now, true),
          ]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];
      await _admins.Received().ListUserSessionsAsync("1", Arg.Any<CancellationToken>());

      Assert.Single(sut.Sessions);
      Assert.True(sut.Sessions[0].Session.IsCurrent);
    }

    // The detail panel's own behaviour: what it offers, and what it deliberately does not.

    [Fact]
    public async Task AddRoleList_OffersOnlyRolesTheUserDoesNotAlreadyHold() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 1, ["Admin"]),
          ]));
      _admins.ListUserSessionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];

      // Admin and Viewer exist; Ada holds Admin, so only Viewer may be added.
      Assert.Equal(["Viewer"], sut.AssignableRoles.Select(role => role.Name));
      Assert.Equal(["Admin"], sut.AssignedRoles.Select(role => role.Name));
    }

    [Fact]
    public async Task Sessions_ShowTheNewestThreeAndCountTheRest() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 5, []),
          ]));
      _admins.ListUserSessionsAsync("1", Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([
            new SessionRow("s1", "c", "d1", "1.1.1.1", now, now, true),
            new SessionRow("s2", "c", "d2", "1.1.1.2", now, now, false),
            new SessionRow("s3", "c", "d3", "1.1.1.3", now, now, false),
            new SessionRow("s4", "c", "d4", "1.1.1.4", now, now, false),
            new SessionRow("s5", "c", "d5", "1.1.1.5", now, now, false),
          ]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];
      await _admins.Received().ListUserSessionsAsync("1", Arg.Any<CancellationToken>());

      Assert.Equal(3, sut.Sessions.Count);
      Assert.Equal(5, sut.SessionCount);
      Assert.Equal("+2 older session(s) not shown", sut.MoreSessions);
    }

    [Fact]
    public async Task SignOutEverywhere_RevokesEverySessionNotOnlyTheVisibleThree() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 5, []),
          ]));
      _admins.ListUserSessionsAsync("1", Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([
            new SessionRow("s1", "c", "d1", "1.1.1.1", now, now, true),
            new SessionRow("s2", "c", "d2", "1.1.1.2", now, now, false),
            new SessionRow("s3", "c", "d3", "1.1.1.3", now, now, false),
            new SessionRow("s4", "c", "d4", "1.1.1.4", now, now, false),
          ]));
      _admins.RevokeSessionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success());
      _dialogs.ShowConfirmAsync(
              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
          .Returns(true);
      // Somebody else's account, so the caller is not signed out along with them.
      _sender.Send(Arg.Any<GetCurrentSessionQuery>())
          .Returns(new SessionDto(true, "admin@x.test", "Admin", null, null, []));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];
      await _admins.Received().ListUserSessionsAsync("1", Arg.Any<CancellationToken>());

      await sut.SignOutEverywhereCommand.ExecuteAsync(null);

      // Four, not the three the panel draws.
      await _admins.Received(4).RevokeSessionAsync(
          "1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CopyEmail_PutsTheAddressOnTheClipboard() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 0, []),
          ]));
      _admins.ListUserSessionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];

      sut.CopyEmailCommand.Execute(null);

      _clipboard.Received(1).SetText("ada@x.test");
    }

    [Fact]
    public async Task ClosingTheDetailPanel_KeepsTheRowSelected() {
      var now = DateTimeOffset.Now;
      _admins.ListUsersAsync(Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<UserRow>>([
            new UserRow("1", "ada@x.test", "Ada", "", "", true, now, now, 0, ["Admin"]),
          ]));
      _admins.ListUserSessionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<SessionRow>>([]));

      var sut = CreateUsers();
      await sut.LoadCommand.ExecuteAsync(null);
      sut.SelectedUser = sut.Users[0];
      Assert.NotEmpty(sut.AssignedRoles);

      Assert.True(sut.IsDetailOpen);

      // Closing hides the panel and leaves the row selected, which is what a list should do.
      sut.CloseDetailCommand.Execute(null);

      Assert.False(sut.IsDetailOpen);
      Assert.NotNull(sut.SelectedUser);

      // Deselecting is the other path, and it empties the panel's state.
      sut.SelectedUser = null;
      Assert.Empty(sut.AssignedRoles);
      Assert.Empty(sut.Sessions);
    }

    private static GrantViewModel Grant(RolePermissionsViewModel sut, string key) {
      return sut.Groups.SelectMany(group => group.All).Single(grant => grant.Key == key);
    }
  }
}
