using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NavigationV1 = Kakehashi.Navigation.V1;

namespace Kakehashi.App.Services {
  // One destination as the deployment says to draw it.
  // Id: The destination's id, which is what the client matches its own pages on.
  // Title: The label, already resolved from any override the deployment set.
  // Icon: A semantic icon name — "note", "people" — not a glyph.
  // IsEnabled: False means the account may not use it, so draw it disabled.
  public sealed record NavigationPlacement(string Id, string Title, string Icon, bool IsEnabled);

  // A heading and the destinations under it, in the order they should be drawn.
  public sealed record NavigationGroup(string Title, IReadOnlyList<NavigationPlacement> Items);

  // A whole navigation pane as the deployment arranged it.
  //
  // Ungrouped: Destinations with no heading, drawn above every group.
  // Groups: The headings, in order. A heading with nothing left in it is not here.
  public sealed record NavigationLayout(
      IReadOnlyList<NavigationPlacement> Ungrouped, IReadOnlyList<NavigationGroup> Groups) {
    // The answer to use when the server has not been asked, or could not be reached.
    public static NavigationLayout None { get; } = new([], []);

    // Whether this layout says anything at all.
    public bool IsEmpty => Ungrouped.Count == 0 && Groups.Count == 0;
  }

  // Where this deployment puts each of the client's destinations.
  //
  // A port so the shell's rules can be tested without a server.
  //
  // The arrangement is the deployment's to decide, not this build's: which heading a screen sits
  // under, in what order, under what label, and whether it is offered at all. That answer has to be
  // the same for every client — including ones built at a different time — which is why it comes
  // over the wire rather than out of a constant in here.
  public interface INavigationLayoutService {
    // The last answer fetched, or NavigationLayout.None before the first.
    NavigationLayout Current { get; }

    // Re-reads the arrangement from the server.
    Task RefreshAsync(CancellationToken cancellationToken);

    // Raised after RefreshAsync replaces the arrangement.
    event EventHandler? Changed;
  }

  // Asks the server how this deployment's navigation pane is arranged.
  //
  // Separate from IPermissionService because they answer different questions of
  // different things. Permissions are about the account and change when a role does; the layout is
  // about the deployment and is the same for everyone. They are fetched at the same moment only
  // because the pane needs both.
  //
  // Nothing here is a security boundary. The server refuses a route on its own, whatever this client
  // was told to draw — the disabled row is a courtesy, not a lock.
  public sealed partial class NavigationLayoutService : INavigationLayoutService {
    private readonly NavigationV1.NavigationService.NavigationServiceClient _client;
    private readonly ILogger<NavigationLayoutService> _logger;

    public NavigationLayoutService(
        NavigationV1.NavigationService.NavigationServiceClient client,
        ILogger<NavigationLayoutService> logger) {
      ArgumentNullException.ThrowIfNull(client);
      ArgumentNullException.ThrowIfNull(logger);
      _client = client;
      _logger = logger;
    }

    public event EventHandler? Changed;

    public NavigationLayout Current { get; private set; } = NavigationLayout.None;

    // Fetches the arrangement and tells whoever is drawing the pane.
    //
    // Failure leaves the previous answer standing, and leaves NavigationLayout.None
    // standing on a first attempt that fails. Both are deliberate: the shell falls back to the
    // arrangement compiled into this build, so an unreachable server costs the deployment's
    // customisations rather than the whole navigation pane. An app that will not draw a menu because
    // a call timed out is worse than one drawing last week's menu.
    public async Task RefreshAsync(CancellationToken cancellationToken) {
      try {
        var reply = await _client
            .GetNavigationAsync(
                new NavigationV1.GetNavigationRequest(), cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        var groups = new List<NavigationGroup>(reply.Groups.Count);
        foreach (var group in reply.Groups) {
          groups.Add(new NavigationGroup(group.Title, ToPlacements(group.Items)));
        }

        Current = new NavigationLayout(ToPlacements(reply.Ungrouped), groups);
        LogApplied(Current.Ungrouped.Count, groups.Count);
        Changed?.Invoke(this, EventArgs.Empty);
      } catch (RpcException exception)
          when (exception.StatusCode == StatusCode.Unauthenticated) {
        // Signing out revokes the token and the refresh that follows the session change lands here.
        // The arrangement is dropped rather than kept: what it describes is a pane for somebody who
        // has left.
        Current = NavigationLayout.None;
        LogSignedOut();
        Changed?.Invoke(this, EventArgs.Empty);
      } catch (RpcException exception) {
        LogFailed(exception.StatusCode, exception);
      }
    }

    private static IReadOnlyList<NavigationPlacement> ToPlacements(
        IReadOnlyList<NavigationV1.Item> items) {
      var placements = new List<NavigationPlacement>(items.Count);
      foreach (var item in items) {
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
}
