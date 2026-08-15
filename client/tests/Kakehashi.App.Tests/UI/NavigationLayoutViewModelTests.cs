using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Services;
using Kakehashi.App.UI;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.UI;

/// <summary>
/// Unit tests for <see cref="NavigationLayoutViewModel"/>: the staging, and the traps it was built
/// around — an arrangement that claims unsaved changes the moment it opens, a move into a heading
/// that has no identifier yet, and an apply that rewrites rows nobody touched.
/// </summary>
public sealed class NavigationLayoutViewModelTests
{
    private readonly INavigationAdminService _admin = Substitute.For<INavigationAdminService>();
    private readonly INavigationLayoutService _layout =
        Substitute.For<INavigationLayoutService>();
    private readonly IAccessAdminService _access = Substitute.For<IAccessAdminService>();
    private readonly INotificationService _notifications =
        Substitute.For<INotificationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IModuleRegistry _registry = Substitute.For<IModuleRegistry>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    private static NavGroupRow Group(string id, string title, int order, bool system = true)
    {
        return new NavGroupRow(id, title, order, system);
    }

    private static NavItemRow Item(
        string id,
        string group,
        int order,
        string title = "",
        string icon = "",
        bool visible = true,
        bool orphan = false,
        bool hideWhenDenied = false,
        string defaultGroup = "utilities",
        int defaultOrder = 10)
    {
        return new NavItemRow(
            id, "notes", group, title, icon, id, "note", order, visible, orphan,
            id + ".access", hideWhenDenied, defaultGroup, defaultOrder);
    }

    private NavigationLayoutViewModel Create(
        IReadOnlyList<NavGroupRow> groups, IReadOnlyList<NavItemRow> items)
    {
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(groups)));
        _admin.ListItemsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(items)));
        _admin.ApplyLayoutAsync(
                Arg.Any<IReadOnlyList<NavGroupSpec>>(),
                Arg.Any<IReadOnlyList<NavItemSpec>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new NavApplyOutcome(0, 0, 0, 0))));
        _access.ListRolesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Success<IReadOnlyList<RoleRow>>([])));
        _dialogs.ShowConfirmAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        return new NavigationLayoutViewModel(
            _admin, _layout, _access, _notifications, _dialogs, _navigation, _registry, _permissions);
    }

    private async Task<NavigationLayoutViewModel> LoadedAsync(
        IReadOnlyList<NavGroupRow> groups, IReadOnlyList<NavItemRow> items)
    {
        var viewModel = Create(groups, items);
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        return viewModel;
    }

    private static NavHeadingNode Heading(NavigationLayoutViewModel viewModel, string id)
    {
        return viewModel.Headings.Single(heading => !heading.IsUnfiled && heading.Id == id);
    }

    private static NavHeadingNode Unfiled(NavigationLayoutViewModel viewModel)
    {
        return viewModel.Headings.Single(heading => heading.IsUnfiled);
    }

    private static NavScreenNode Screen(NavigationLayoutViewModel viewModel, string id)
    {
        return viewModel.Headings
            .SelectMany(heading => heading.Screens)
            .Single(screen => screen.Id == id);
    }

    /// <summary>
    /// A screen with no heading, and a leftover from a module that is not part of this build, both
    /// need somewhere to be — and this is the one screen where anybody can discover the leftover
    /// exists.
    /// </summary>
    [Fact]
    public async Task Load_PutsEverythingSomewhere()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [
              Item("notes", "utilities", 10),
        Item("nowhere", string.Empty, 20),
        Item("a-module-this-build-lost", string.Empty, 30, orphan: true),
            ]);

        Assert.Single(Heading(viewModel, "utilities").Screens);
        Assert.Equal(2, Unfiled(viewModel).Screens.Count);
        // The bucket is last, and it is not a heading anybody can act on.
        Assert.True(viewModel.Headings[^1].IsUnfiled);
        Assert.False(viewModel.Headings[^1].CanDelete);
        Assert.False(viewModel.Headings[^1].CanRename);
    }

    /// <summary>
    /// Ordered the way the pane orders. The server lists declared destinations in declaration order and
    /// orphans after them, which is not the order they are drawn in.
    /// </summary>
    [Fact]
    public async Task Load_OrdersScreensTheWayThePaneWill()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("second", "utilities", 20), Item("first", "utilities", 10)]);

        var screens = Heading(viewModel, "utilities").Screens;
        Assert.Equal("first", screens[0].Id);
        Assert.Equal("second", screens[1].Id);
    }

    /// <summary>
    /// Nothing is unsaved the moment the screen opens — including on a deployment whose stored orders
    /// are not multiples of ten. A positional comparison is what makes that true; comparing against a
    /// freshly renumbered 10 and 20 would have claimed two changes nobody made.
    /// </summary>
    [Fact]
    public async Task Load_ClaimsNoChangesEvenWhenStoredOrdersAreOdd()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 7)],
            [Item("notes", "utilities", 5), Item("activity", "utilities", 7)]);

        Assert.Equal(0, viewModel.ChangedCount);
        Assert.False(viewModel.HasChanges);
        Assert.All(
            viewModel.Headings.SelectMany(heading => heading.Screens),
            screen => Assert.False(screen.IsModified));
    }

    [Fact]
    public async Task MovingAScreenStagesOneChangeAndWritesNothing()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10), Group("administration", "Administration", 20)],
            [Item("notes", "utilities", 10)]);

        viewModel.MoveScreen(Screen(viewModel, "notes"), Heading(viewModel, "administration"), 0);

        Assert.Equal(1, viewModel.ChangedCount);
        Assert.True(Screen(viewModel, "notes").IsModified);
        Assert.Contains("moved to Administration", viewModel.ChangeSummary, StringComparison.Ordinal);
        await _admin.DidNotReceive().ApplyLayoutAsync(
            Arg.Any<IReadOnlyList<NavGroupSpec>>(),
            Arg.Any<IReadOnlyList<NavItemSpec>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A heading created on screen has no identifier until the apply comes back, so comparing headings
    /// by identifier read "moved from unfiled into a brand-new heading" as no change at all — both
    /// sides being empty. The comparison is by reference for exactly this case.
    /// </summary>
    [Fact]
    public async Task MovingAnUnfiledScreenIntoABrandNewHeadingIsNoticed()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("nowhere", string.Empty, 10)]);

        viewModel.NewHeadingCommand.Execute(parameter: null);
        var created = viewModel.Headings.Single(heading => heading.IsNew);
        viewModel.MoveScreen(Screen(viewModel, "nowhere"), created, 0);

        Assert.True(Screen(viewModel, "nowhere").IsModified);
    }

    /// <summary>
    /// A new heading is given an identifier here rather than left for the server to derive, because a
    /// screen dropped into it has to name it — and a title-derived identifier is not knowable until the
    /// apply has already happened.
    /// </summary>
    [Fact]
    public async Task ApplyingANewHeadingNamesItSoItsScreensCanReferToIt()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("notes", "utilities", 10)]);

        viewModel.NewHeadingCommand.Execute(parameter: null);
        var created = viewModel.Headings.Single(heading => heading.IsNew);
        viewModel.MoveScreen(Screen(viewModel, "notes"), created, 0);

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        await _admin.Received(1).ApplyLayoutAsync(
            Arg.Is<IReadOnlyList<NavGroupSpec>>(groups =>
                groups != null && groups.Any(group => group.Id.Length > 0 && group.Title == "New heading")),
            Arg.Is<IReadOnlyList<NavItemSpec>>(items =>
                items != null && items.Single(item => item.Id == "notes").GroupId == created.Id),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Renumbering only where the order moved: renumbering everything would rewrite rows nobody
    /// touched.
    /// </summary>
    [Fact]
    public async Task ApplyKeepsTheOrdersOfHeadingsNobodyReordered()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10), Group("administration", "Administration", 20)],
            [
              Item("notes", "utilities", 5),
        Item("activity", "utilities", 7),
        Item("users", "administration", 3),
            ]);

        // One heading re-ordered; the other untouched.
        var utilities = Heading(viewModel, "utilities");
        viewModel.MoveScreen(Screen(viewModel, "activity"), utilities, 0);

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        await _admin.Received(1).ApplyLayoutAsync(
            Arg.Any<IReadOnlyList<NavGroupSpec>>(),
            Arg.Is<IReadOnlyList<NavItemSpec>>(items =>
                // Renumbered, because this heading's sequence changed.
                items != null
                && items.Single(item => item.Id == "activity").SortOrder == 10
                && items.Single(item => item.Id == "notes").SortOrder == 20
                // Left alone, because that one's did not.
                && items.Single(item => item.Id == "users").SortOrder == 3),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Discard restores the placement, which no node can do for itself: a node can put its own name
    /// back, but nothing on it knows which heading it came from.
    /// </summary>
    [Fact]
    public async Task DiscardPutsEverythingBackIncludingWhereScreensSat()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10), Group("administration", "Administration", 20)],
            [Item("notes", "utilities", 10)]);

        Screen(viewModel, "notes").Title = "Scratchpad";
        viewModel.MoveScreen(Screen(viewModel, "notes"), Heading(viewModel, "administration"), 0);
        Assert.True(viewModel.HasChanges);

        viewModel.DiscardCommand.Execute(parameter: null);

        Assert.Equal(0, viewModel.ChangedCount);
        Assert.Single(Heading(viewModel, "utilities").Screens);
        Assert.Empty(Heading(viewModel, "administration").Screens);
        Assert.Equal(string.Empty, Screen(viewModel, "notes").Title);
    }

    [Fact]
    public async Task DeletingAHeadingUnfilesWhatWasUnderItAndStagesTheRemoval()
    {
        var viewModel = await LoadedAsync(
            [Group("monitoring", "Monitoring", 30, system: false)],
            [Item("notes", "monitoring", 10)]);

        await viewModel.DeleteHeadingCommand.ExecuteAsync(Heading(viewModel, "monitoring"));

        Assert.DoesNotContain(viewModel.Headings, heading => heading.Id == "monitoring");
        Assert.Single(Unfiled(viewModel).Screens);
        // Staged: the removal is part of the next apply, not a call of its own.
        await _admin.DidNotReceive().DeleteGroupAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A heading the product ships offers no delete control: the server refuses the delete, so a
    /// button's only possible outcome would be the error bar.
    /// </summary>
    [Fact]
    public async Task AHeadingTheProductShipsCannotBeDeleted()
    {
        var viewModel = await LoadedAsync([Group("administration", "Administration", 20)], []);

        Assert.False(Heading(viewModel, "administration").CanDelete);
        Assert.True(Heading(viewModel, "administration").CanRename);
    }

    /// <summary>
    /// A row the server refuses to hide (hideWhenDenied) is offered no switch at all: a switch
    /// whose only possible outcome is the error bar is discoverable only by trying it.
    /// </summary>
    [Fact]
    public async Task AScreenThatManagesThePaneOffersNoWayToHideIt()
    {
        var viewModel = await LoadedAsync(
            [Group("administration", "Administration", 20)],
            [Item("navigation.layout", "administration", 30, hideWhenDenied: true)]);

        Assert.False(Screen(viewModel, "navigation.layout").CanHide);
    }

    /// <summary>
    /// Nothing was written — the server validates the whole arrangement before it writes any of it — so
    /// what is on screen is still what the person asked for, and throwing it away would make them do it
    /// again.
    /// </summary>
    [Fact]
    public async Task AFailedApplyKeepsEverythingStaged()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10), Group("administration", "Administration", 20)],
            [Item("notes", "utilities", 10)]);
        viewModel.MoveScreen(Screen(viewModel, "notes"), Heading(viewModel, "administration"), 0);

        _admin.ApplyLayoutAsync(
                Arg.Any<IReadOnlyList<NavGroupSpec>>(),
                Arg.Any<IReadOnlyList<NavItemSpec>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<NavApplyOutcome>(
                new Error("Invalid", "Administration is one of the headings this product ships."))));

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(1, viewModel.ChangedCount);
        Assert.Single(Heading(viewModel, "administration").Screens);
    }

    /// <summary>Reset puts a screen back where the code puts it, under the name the code gives it.</summary>
    [Fact]
    public async Task ResetPutsAScreenBackWhereTheCodePutsIt()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10), Group("administration", "Administration", 20)],
            [Item("notes", "administration", 99, title: "Scratchpad", icon: "people", visible: false)]);

        viewModel.SelectedScreen = Screen(viewModel, "notes");
        viewModel.ResetScreenCommand.Execute(parameter: null);

        var screen = Screen(viewModel, "notes");
        Assert.Equal(string.Empty, screen.Title);
        Assert.Equal(string.Empty, screen.Icon);
        Assert.True(screen.IsVisible);
        Assert.Equal("utilities", screen.Heading?.Id);
    }

    /// <summary>
    /// Removing a leftover row is not an arrangement — the row stops existing — so it takes effect at
    /// once rather than waiting for Apply, which is what its own confirmation says.
    /// </summary>
    [Fact]
    public async Task RemovingALeftoverRowHappensAtOnce()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("a-module-this-build-lost", string.Empty, 30, orphan: true)]);
        _admin.DeleteItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        await viewModel.DeleteOrphanCommand.ExecuteAsync(
            Screen(viewModel, "a-module-this-build-lost"));

        await _admin.Received(1).DeleteItemAsync(
            "a-module-this-build-lost", Arg.Any<CancellationToken>());
    }

    /// <summary>A screen this build still has offers no such button, and the command refuses it.</summary>
    [Fact]
    public async Task RemovingARowTheBuildStillHasIsRefused()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("notes", "utilities", 10)]);

        await viewModel.DeleteOrphanCommand.ExecuteAsync(Screen(viewModel, "notes"));

        await _admin.DidNotReceive().DeleteItemAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Previewing as a role asks the server, because only the server knows what a role may reach. It
    /// answers from what is stored, which the note on screen has to say.
    /// </summary>
    [Fact]
    public async Task PreviewingAsARoleAsksTheServer()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("notes", "utilities", 10)]);
        _admin.PreviewLayoutAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(NavigationLayout.None)));

        viewModel.PreviewRole = new NavPreviewRole("reader", "Reader");

        await _admin.Received(1).PreviewLayoutAsync("reader", Arg.Any<CancellationToken>());
        Assert.Contains("from what is saved", viewModel.PreviewNote, StringComparison.Ordinal);
    }

    /// <summary>With no role it is the staged arrangement, which is the only preview that can show
    /// unapplied edits.</summary>
    [Fact]
    public async Task PreviewingYourOwnPaneAsksNobody()
    {
        var viewModel = await LoadedAsync(
            [Group("utilities", "Utilities", 10)],
            [Item("notes", "utilities", 10)]);

        await _admin.DidNotReceive().PreviewLayoutAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Contains("not applied yet", viewModel.PreviewNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed read says why and builds nothing. Note the arrange order: the stubs are set up by
    /// Create, so the failure has to be installed after it — an earlier version of this test set it
    /// first, had it overwritten, and passed a successful empty load off as a failure.
    /// </summary>
    [Fact]
    public async Task AFailedLoadIsReportedAndBuildsNothing()
    {
        var viewModel = Create([], []);
        _admin.ListGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Failure<IReadOnlyList<NavGroupRow>>(new Error("Unavailable", "No server."))));

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Empty(viewModel.Headings);
        _notifications.Received().Show(
            "No server.",
            Arg.Any<Microsoft.UI.Xaml.Controls.InfoBarSeverity>(),
            Arg.Any<string?>());
    }

    /// <summary>
    /// An account with nothing arranged still gets the unfiled bucket, because that is where a screen
    /// with no heading would go the moment one appears.
    /// </summary>
    [Fact]
    public async Task AnEmptyArrangementStillHasSomewhereToPutThings()
    {
        var viewModel = await LoadedAsync([], []);

        Assert.Single(viewModel.Headings);
        Assert.True(viewModel.Headings[0].IsUnfiled);
        Assert.Equal(0, viewModel.ChangedCount);
    }
}
