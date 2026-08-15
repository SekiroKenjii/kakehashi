using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity;
using Kakehashi.Modules.Activity.UI.ViewModels;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Activity.UI.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ActivityViewModel"/>: the grouping and collapsing it decides for
/// itself, the counts it must take from the server rather than compute, and the two failures that
/// have to clear what is on screen.
/// </summary>
public sealed class ActivityViewModelTests
{
    private static readonly DateTimeOffset _noon =
        new(2026, 8, 11, 12, 0, 0, DateTimeOffset.Now.Offset);

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IFileSaveService _files = Substitute.For<IFileSaveService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly INotificationService _notifications =
        Substitute.For<INotificationService>();
    private readonly IAccountScreen _accountScreen = Substitute.For<IAccountScreen>();

    private ActivityViewModel CreateViewModel()
    {
        return new ActivityViewModel(_sender, _files, _clipboard, _notifications, _accountScreen);
    }

    /// <summary>Answers every request with the same page.</summary>
    private void Returns(Result<ActivityPageDto> result)
    {
        _sender.Send(Arg.Is<GetActivityQuery>(query => query != null), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
    }

    /// <summary>Answers the first request with one page and every later one with another.</summary>
    private void Returns(ActivityPageDto first, ActivityPageDto rest)
    {
        _sender.Send(Arg.Is<GetActivityQuery>(query => query != null), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Result.Success(
                ((GetActivityQuery)call[0]!).Filter.PageToken.Length == 0 ? first : rest)));
    }

    private static ActivityPageDto Page(
        IReadOnlyList<ActivityEntryDto> entries,
        string next = "",
        int total = 0,
        Dictionary<string, int>? counts = null,
        Dictionary<string, int>? kinds = null)
    {
        return new ActivityPageDto(
            entries, next, total == 0 ? entries.Count : total,
            counts ?? [], kinds ?? [], RetentionDays: 90);
    }

    private static ActivityEntryDto Entry(
        string kind,
        DateTimeOffset at,
        string session = "session-1",
        string platform = "Windows",
        string id = "id")
    {
        return new ActivityEntryDto(
            id, kind, ActivityCategories.SignIn, session, "Kakehashi/1 (Windows NT 10.0)", platform,
            "10.0.0.1", at);
    }

    [Fact]
    public async Task Load_GroupsRowsByTheReadersOwnDay()
    {
        Returns(Result.Success(Page([
            Entry(ActivityKinds.SignedIn, _noon),
            Entry(ActivityKinds.PasswordChanged, _noon.AddHours(-2), session: ""),
            Entry(ActivityKinds.SignedIn, _noon.AddDays(-1)),
        ])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, viewModel.Days.Count);
        Assert.Equal(2, viewModel.Days[0].Items.Count);
        Assert.Single(viewModel.Days[1].Items);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>
    /// Nine sign-outs from one session are one thing that happened. Collapsing only a consecutive run
    /// is what stops the grouping from reordering the feed.
    /// </summary>
    [Fact]
    public async Task Load_CollapsesAConsecutiveRunFromOneSession()
    {
        Returns(Result.Success(Page([
            Entry(ActivityKinds.SignedOut, _noon),
            Entry(ActivityKinds.SignedOut, _noon.AddMinutes(-2)),
            Entry(ActivityKinds.SignedOut, _noon.AddMinutes(-4)),
            Entry(ActivityKinds.SignedIn, _noon.AddMinutes(-6)),
        ])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        var rows = viewModel.Days[0].Items;
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsBurst);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal("×3", rows[0].CountText);
        Assert.False(rows[1].IsBurst);
    }

    [Fact]
    public async Task Load_DoesNotCollapseAcrossSessionsOrAcrossTheWindow()
    {
        Returns(Result.Success(Page([
            Entry(ActivityKinds.SignedOut, _noon, session: "session-1"),
            Entry(ActivityKinds.SignedOut, _noon.AddMinutes(-1), session: "session-2"),
            Entry(ActivityKinds.SignedOut, _noon.AddHours(-3), session: "session-2"),
        ])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal(3, viewModel.Days[0].Items.Count);
        Assert.All(viewModel.Days[0].Items, row => Assert.False(row.IsBurst));
    }

    /// <summary>
    /// A fact with no session is never collapsed even when it repeats: two password changes are two
    /// decisions, not one event reported twice.
    /// </summary>
    [Fact]
    public async Task Load_NeverCollapsesFactsThatHaveNoSession()
    {
        Returns(Result.Success(Page([
            Entry(ActivityKinds.PasswordChanged, _noon, session: ""),
            Entry(ActivityKinds.PasswordChanged, _noon.AddMinutes(-1), session: ""),
        ])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, viewModel.Days[0].Items.Count);
    }

    /// <summary>
    /// The chips show what the server counted over the whole range, not what happens to be loaded.
    /// </summary>
    [Fact]
    public async Task Load_TakesTheChipCountsFromTheServer()
    {
        Returns(Result.Success(Page(
            [Entry(ActivityKinds.SignedIn, _noon)],
            total: 214,
            counts: new Dictionary<string, int> {
                [ActivityCategories.SignIn] = 200,
                [ActivityCategories.Security] = 12,
                [ActivityCategories.System] = 2,
            })));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal(214, Chip(viewModel, ActivityCategories.All).Count);
        Assert.Equal(200, Chip(viewModel, ActivityCategories.SignIn).Count);
        Assert.Equal(12, Chip(viewModel, ActivityCategories.Security).Count);
        Assert.Contains("Showing 1 of 214 events", viewModel.CountSummary, StringComparison.Ordinal);
        Assert.Contains("kept for 90 days", viewModel.CountSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The server sends the counts only with the first page, so a later page must not blank them.
    /// </summary>
    [Fact]
    public async Task LoadMore_KeepsTheCountsTheFirstPageBrought()
    {
        Returns(
            first: Page([Entry(ActivityKinds.SignedIn, _noon)], next: "token", total: 214,
                counts: new Dictionary<string, int> { [ActivityCategories.SignIn] = 200 }),
            rest: Page([Entry(ActivityKinds.SignedIn, _noon.AddDays(-2), id: "id-2")], total: 214));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);
        await viewModel.LoadMoreCommand.ExecuteAsync(parameter: null);

        Assert.Equal(200, Chip(viewModel, ActivityCategories.SignIn).Count);
        Assert.Equal(214, Chip(viewModel, ActivityCategories.All).Count);
        // And the second page was appended rather than replacing the first.
        Assert.Equal(2, viewModel.Days.Sum(day => day.Items.Count));
    }

    [Fact]
    public async Task LoadMore_DoesNothingWhenThereIsNoNextPage()
    {
        Returns(Result.Success(Page([Entry(ActivityKinds.SignedIn, _noon)])));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);
        _sender.ClearReceivedCalls();

        await viewModel.LoadMoreCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.HasMore);
        await _sender.DidNotReceive().Send(
            Arg.Is<GetActivityQuery>(query => query != null), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Search runs on the server, so it applies when submitted. Emptying the box applies at once:
    /// nobody expects to press Enter to stop filtering.
    /// </summary>
    [Fact]
    public async Task Search_AppliesOnSubmitAndClearsImmediately()
    {
        Returns(Result.Success(Page([Entry(ActivityKinds.SignedIn, _noon)])));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        viewModel.SearchText = "203.0.113";
        await viewModel.SearchCommand.ExecuteAsync(parameter: null);

        await _sender.Received(1).Send(
            Arg.Is<GetActivityQuery>(query => query != null && query.Filter.Search == "203.0.113"),
            Arg.Any<CancellationToken>());

        viewModel.SearchText = string.Empty;

        await _sender.Received(2).Send(
            Arg.Is<GetActivityQuery>(query => query != null && query.Filter.Search.Length == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectingAChipNarrowsTheNextRequest()
    {
        Returns(Result.Success(Page([Entry(ActivityKinds.SignedIn, _noon)])));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        await viewModel.SelectCategoryCommand.ExecuteAsync(
            Chip(viewModel, ActivityCategories.Security));

        Assert.True(Chip(viewModel, ActivityCategories.Security).IsSelected);
        Assert.False(Chip(viewModel, ActivityCategories.All).IsSelected);
        await _sender.Received(1).Send(
            Arg.Is<GetActivityQuery>(
                query => query != null && query.Filter.Category == ActivityCategories.Security),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A page left open across a sign-out keeps refreshing, and a feed that held its rows would go on
    /// showing the previous account's devices and addresses to whoever signs in next.
    /// </summary>
    [Fact]
    public async Task Load_WhenTheSessionIsGone_ClearsWhatIsOnScreen()
    {
        Returns(Result.Success(Page([Entry(ActivityKinds.SignedIn, _noon)])));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Returns(Result.Failure<ActivityPageDto>(ActivityErrors.NotSignedIn));
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Empty(viewModel.Days);
        Assert.True(viewModel.HasError);
    }

    /// <summary>An unreadable page position means what is on screen cannot be continued from.</summary>
    [Fact]
    public async Task LoadMore_WhenThePositionIsLost_ClearsAndStopsOfferingMore()
    {
        Returns(
            first: Page([Entry(ActivityKinds.SignedIn, _noon)], next: "token"),
            rest: Page([]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Returns(Result.Failure<ActivityPageDto>(ActivityErrors.PageLost));
        await viewModel.LoadMoreCommand.ExecuteAsync(parameter: null);

        Assert.Empty(viewModel.Days);
        Assert.False(viewModel.HasMore);
    }

    /// <summary>
    /// A network blip is different: the rows are still true, and throwing them away would make a
    /// flaky connection look like an empty history.
    /// </summary>
    [Fact]
    public async Task Load_WhenTheNetworkFails_KeepsTheRowsItAlreadyHas()
    {
        Returns(Result.Success(Page([Entry(ActivityKinds.SignedIn, _noon)])));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Returns(Result.Failure<ActivityPageDto>(ActivityErrors.RequestFailed));
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.NotEmpty(viewModel.Days);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task Load_WithNothingToShow_IsEmptyRatherThanFailed()
    {
        Returns(Result.Success(Page([])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    /// The refused-sign-in card states a number the server counted. Deriving it from the Security
    /// total would be wrong, because that total also holds password changes.
    /// </summary>
    [Fact]
    public async Task Load_ReportsRefusedSignInsFromTheServersOwnCount()
    {
        Returns(Result.Success(Page(
            [Entry(ActivityKinds.SignedIn, _noon)],
            total: 30,
            counts: new Dictionary<string, int> { [ActivityCategories.Security] = 4 },
            kinds: new Dictionary<string, int> {
                [ActivityKinds.FailedSignIn] = 1,
                [ActivityKinds.PasswordChanged] = 3,
            })));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        var refused = viewModel.StatCards.Single(card => card.Label == "REFUSED SIGN-INS");
        Assert.Equal("1", refused.Value);
    }

    [Fact]
    public async Task Export_WithNothingLoaded_SaysSoAndAsksForNoFile()
    {
        var viewModel = CreateViewModel();

        await viewModel.ExportCommand.ExecuteAsync(parameter: null);

        _notifications.Received(1).Show(
            "There is nothing to export.", InfoBarSeverity.Informational, Arg.Any<string?>());
        await _files.DidNotReceive().PickSaveLocationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// The Account page belongs to another module, so the only join available is its navigation key.
    /// When that fails, saying where to go beats a link that silently does nothing.
    /// </summary>
    [Fact]
    public void SecureAccount_WhenThatPageIsNotMounted_SaysWhereToGo()
    {
        _accountScreen.Open().Returns(false);
        var viewModel = CreateViewModel();

        viewModel.SecureAccountCommand.Execute(parameter: null);

        _notifications.Received(1).Show(
            Arg.Is<string>(message => message != null && message.Contains("Account", StringComparison.Ordinal)),
            InfoBarSeverity.Informational,
            Arg.Any<string?>());
    }

    private static ActivityChip Chip(ActivityViewModel viewModel, string category)
    {
        return viewModel.Chips.Single(chip => chip.Category == category);
    }
}
