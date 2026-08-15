using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NavigationV1 = __ROOT_NAMESPACE__.Navigation.V1;

namespace __ROOT_NAMESPACE__.App.Services;

/// <summary>One destination as the deployment says to draw it.</summary>
/// <param name="Id">The destination's id, which is what the client matches its own pages on.</param>
/// <param name="Title">The label, already resolved from any override the deployment set.</param>
/// <param name="Icon">A semantic icon name — "note", "people" — not a glyph.</param>
/// <param name="IsEnabled">False means the account may not use it, so draw it disabled.</param>
public sealed record NavigationPlacement(string Id, string Title, string Icon, bool IsEnabled);

/// <summary>A heading and the destinations under it, in the order they should be drawn.</summary>
public sealed record NavigationGroup(string Title, IReadOnlyList<NavigationPlacement> Items);

/// <summary>
/// A whole navigation pane as the deployment arranged it.
/// </summary>
/// <param name="Ungrouped">Destinations with no heading, drawn above every group.</param>
/// <param name="Groups">The headings, in order. A heading with nothing left in it is not here.</param>
public sealed record NavigationLayout(
    IReadOnlyList<NavigationPlacement> Ungrouped, IReadOnlyList<NavigationGroup> Groups)
{
    /// <summary>The answer to use when the server has not been asked, or could not be reached.</summary>
    public static NavigationLayout None { get; } = new([], []);

    public bool IsEmpty => Ungrouped.Count == 0 && Groups.Count == 0;
}

/// <summary>Where this deployment puts each of the client's destinations.</summary>
/// <remarks>
/// A port so the shell's rules can be tested without a server.
/// <para>
/// The arrangement is the deployment's to decide, not this build's: which heading a screen sits
/// under, in what order, under what label, and whether it is offered at all. That answer has to be
/// the same for every client — including ones built at a different time — which is why it comes
/// over the wire rather than out of a constant in here.
/// </para>
/// </remarks>
public interface INavigationLayoutService
{
    /// <summary>The last answer fetched, or <see cref="NavigationLayout.None"/> before the first.</summary>
    NavigationLayout Current { get; }

    /// <summary>Re-reads the arrangement from the server.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Raised after <see cref="RefreshAsync"/> replaces the arrangement.</summary>
    event EventHandler? Changed;
}

/// <summary>Asks the server how this deployment's navigation pane is arranged.</summary>
/// <remarks>
/// Separate from <see cref="IPermissionService"/>: permissions are about the account and change
/// when a role does; the layout is about the deployment and is the same for everyone. Nothing here
/// is a security boundary — the server refuses a route on its own, whatever this client was told
/// to draw; the disabled row is a courtesy, not a lock.
/// </remarks>
public sealed partial class NavigationLayoutService : INavigationLayoutService
{
    private readonly NavigationV1.NavigationService.NavigationServiceClient _client;
    private readonly ILogger<NavigationLayoutService> _logger;

    public NavigationLayoutService(
        NavigationV1.NavigationService.NavigationServiceClient client,
        ILogger<NavigationLayoutService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public NavigationLayout Current { get; private set; } = NavigationLayout.None;

    /// <summary>Fetches the arrangement and tells whoever is drawing the pane.</summary>
    /// <remarks>
    /// Failure leaves the previous answer standing, and leaves <see cref="NavigationLayout.None"/>
    /// standing on a first attempt that fails. Both are deliberate: the shell falls back to the
    /// arrangement compiled into this build, so an unreachable server costs the deployment's
    /// customisations rather than the whole navigation pane.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _client
                .GetNavigationAsync(
                    new NavigationV1.GetNavigationRequest(), cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            var groups = new List<NavigationGroup>(reply.Groups.Count);
            foreach (var group in reply.Groups)
            {
                groups.Add(new NavigationGroup(group.Title, ToPlacements(group.Items)));
            }

            Current = new NavigationLayout(ToPlacements(reply.Ungrouped), groups);
            LogApplied(Current.Ungrouped.Count, groups.Count);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (RpcException exception)
            when (exception.StatusCode == StatusCode.Unauthenticated)
        {
            // Signing out revokes the token and the refresh that follows lands here. The arrangement is
            // dropped, not kept: what it describes is a pane for somebody who has left.
            Current = NavigationLayout.None;
            LogSignedOut();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (RpcException exception)
        {
            LogFailed(exception.StatusCode, exception);
        }
    }

    private static IReadOnlyList<NavigationPlacement> ToPlacements(
        IReadOnlyList<NavigationV1.Item> items)
    {
        var placements = new List<NavigationPlacement>(items.Count);
        foreach (var item in items)
        {
            placements.Add(new NavigationPlacement(item.Id, item.Title, item.Icon, item.Enabled));
        }

        return placements;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Navigation arrangement applied: {Ungrouped} ungrouped, {Groups} headings.")]
    private partial void LogApplied(int ungrouped, int groups);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The navigation arrangement could not be fetched ({Status}); the pane falls back "
            + "to the one compiled into this build.")]
    private partial void LogFailed(StatusCode status, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No signed-in caller; the navigation arrangement was dropped.")]
    private partial void LogSignedOut();
}
