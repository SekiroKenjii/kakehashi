using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Services;
using Kakehashi.App.UI;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.UI {
  /// <summary>
  /// Unit tests for <see cref="NavigationLayoutViewModel"/>: the reorder swap, its bounds, and the
  /// paths that used to move the wrong row or report work that did not happen.
  /// </summary>
  /// <remarks>
  /// The screen had no test file at all, while its 473 lines contained the neighbour swap, a
  /// conflict-avoiding title generator, and every failure path. Two live defects came out of that
  /// gap: reordering from an orphan row moved an unrelated destination, and the two-write swap left
  /// both rows on the same number when the second write failed.
  /// </remarks>
  public sealed class NavigationLayoutViewModelTests {
    private readonly INavigationAdminService _admin = Substitute.For<INavigationAdminService>();
    private readonly INavigationLayoutService _layout =
        Substitute.For<INavigationLayoutService>();
    private readonly INotificationService _notifications =
        Substitute.For<INotificationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private static NavGroupRow Group(string id, string title, int order, bool system = true) {
      return new NavGroupRow(id, title, order, system);
    }

    private static NavItemRow Item(
        string id, string group, int order, bool orphan = false, string module = "notes") {
      return new NavItemRow(
          id, module, group, string.Empty, string.Empty, id, string.Empty, order,
          IsVisible: true, IsOrphan: orphan, RequiredPermission: module + ".access",
          HideWhenDenied: false);
    }

    private NavigationLayoutViewModel Create(
        IReadOnlyList<NavGroupRow> groups, IReadOnlyList<NavItemRow> items) {
      _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Success(groups)));
      _admin.ListItemsAsync(Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Success(items)));
      _admin.MoveItemAsync(
              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
          .Returns(call => Task.FromResult(Result.Success(
              Item(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<int>(2)))));
      _dialogs.ShowConfirmAsync(
              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
          .Returns(Task.FromResult(true));

      return new NavigationLayoutViewModel(_admin, _layout, _notifications, _dialogs);
    }

    private async Task<NavigationLayoutViewModel> LoadedAsync(
        IReadOnlyList<NavGroupRow> groups, IReadOnlyList<NavItemRow> items) {
      var viewModel = Create(groups, items);
      await viewModel.LoadCommand.ExecuteAsync(parameter: null);
      return viewModel;
    }

    /// <summary>
    /// Reordering swaps positions with the neighbour rather than renumbering the heading.
    /// </summary>
    /// <remarks>
    /// Renumbering would rewrite every row under the heading, so a stale screen could push somebody
    /// else's placement around while claiming to move one item.
    /// </remarks>
    [Fact]
    public async Task MoveDown_SwapsPositionsWithTheNeighbourAndNothingElse() {
      var viewModel = await LoadedAsync(
          [Group("utilities", "Utilities", 10)],
          [Item("notes", "utilities", 10), Item("activity", "utilities", 20)]);

      var notes = viewModel.Destinations.Single(row => row.Id == "notes");
      await notes.MoveDownCommand.ExecuteAsync(parameter: null);

      await _admin.Received(1).MoveItemAsync("notes", "utilities", 20, Arg.Any<CancellationToken>());
      await _admin.Received(1)
          .MoveItemAsync("activity", "utilities", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveUp_AtTheTopDoesNothing() {
      var viewModel = await LoadedAsync(
          [Group("utilities", "Utilities", 10)],
          [Item("notes", "utilities", 10), Item("activity", "utilities", 20)]);

      var notes = viewModel.Destinations.Single(row => row.Id == "notes");
      await notes.MoveUpCommand.ExecuteAsync(parameter: null);

      await _admin.DidNotReceive().MoveItemAsync(
          Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An orphan sits in no heading, so it has no neighbours to swap with — and used to swap with
    /// an unrelated destination because IndexOf returned -1 and -1 plus one is a valid index.
    /// </summary>
    [Fact]
    public async Task ReorderingAnOrphanTouchesNothing() {
      var viewModel = await LoadedAsync(
          [Group("utilities", "Utilities", 10)],
          [Item("notes", "utilities", 10), Item("a-module-this-build-lost", "", 0, orphan: true)]);

      var orphan = viewModel.Destinations.Single(row => row.IsOrphan);
      await orphan.MoveDownCommand.ExecuteAsync(parameter: null);
      await orphan.MoveUpCommand.ExecuteAsync(parameter: null);

      await _admin.DidNotReceive().MoveItemAsync(
          Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Two destinations an administrator gave the same number still reorder.
    /// </summary>
    /// <remarks>
    /// Swapping equal numbers is a no-op, so one of them is nudged. Without that, the buttons did
    /// nothing and said nothing on exactly the rows somebody was trying to separate.
    /// </remarks>
    [Fact]
    public async Task ReorderingRowsThatShareAPositionStillMovesOne() {
      var viewModel = await LoadedAsync(
          [Group("utilities", "Utilities", 10)],
          [Item("notes", "utilities", 10), Item("activity", "utilities", 10)]);

      // Equal positions tie-break on id, so "activity" is first; moving it down is the case where
      // a naive swap of two identical numbers would change nothing.
      var activity = viewModel.Destinations.Single(row => row.Id == "activity");
      await activity.MoveDownCommand.ExecuteAsync(parameter: null);

      await _admin.Received(2).MoveItemAsync(
          Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A failed load reports the reason rather than showing an empty screen.</summary>
    [Fact]
    public async Task AFailedLoadIsReportedAndLeavesTheScreenEmpty() {
      _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(
              Result.Failure<IReadOnlyList<NavGroupRow>>(new Error("Unavailable", "No server."))));

      var viewModel = new NavigationLayoutViewModel(_admin, _layout, _notifications, _dialogs);
      await viewModel.LoadCommand.ExecuteAsync(parameter: null);

      Assert.Empty(viewModel.Headings);
      Assert.Empty(viewModel.Destinations);
      _notifications.Received().Show("No server.", Arg.Any<InfoBarSeverity>(), Arg.Any<string?>());
    }

    /// <summary>
    /// A second heading created without renaming the first gets a name of its own.
    /// </summary>
    /// <remarks>
    /// Titles are unique in the database, so a second "New heading" would come back as a conflict
    /// rather than as a heading — a failure the person did not cause and cannot act on.
    /// </remarks>
    [Fact]
    public async Task CreatingASecondHeadingDoesNotCollideWithTheFirst() {
      _admin.CreateGroupAsync(
              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
          .Returns(call => Task.FromResult(Result.Success(
              Group("new-heading-2", call.ArgAt<string>(1), call.ArgAt<int>(2), system: false))));

      var viewModel = await LoadedAsync(
          [Group("new-heading", "New heading", 30, system: false)], []);
      await viewModel.NewHeadingCommand.ExecuteAsync(parameter: null);

      await _admin.Received(1).CreateGroupAsync(
          Arg.Any<string>(),
          Arg.Is<string>(title => title != "New heading"),
          Arg.Any<int>(),
          Arg.Any<CancellationToken>());
    }

    /// <summary>Deleting a heading asks first, and does nothing when the answer is no.</summary>
    [Fact]
    public async Task DeletingAHeadingIsCancellable() {
      var viewModel = await LoadedAsync(
          [Group("monitoring", "Monitoring", 30, system: false)], []);
      viewModel.SelectedHeading = viewModel.Headings.Single();

      // After the load, because Create stubs the confirmation to yes for every other test.
      _dialogs.ShowConfirmAsync(
              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
          .Returns(Task.FromResult(false));

      await viewModel.DeleteHeadingCommand.ExecuteAsync(parameter: null);

      await _admin.DidNotReceive().DeleteGroupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
  }
}
