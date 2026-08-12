using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NavigationV1 = Kakehashi.Navigation.V1;

namespace Kakehashi.App.Services {
  // Icon is a semantic name — "note", "people" — not a glyph. IsEnabled false means the account
  // may not use the destination, so it is drawn disabled rather than dropped.
  public sealed record NavigationPlacement(string Id, string Title, string Icon, bool IsEnabled);

  // Items are in the order they should be drawn.
  public sealed record NavigationGroup(string Title, IReadOnlyList<NavigationPlacement> Items);

  // Ungrouped is drawn above every group. An empty heading is not in Groups at all.
  public sealed record NavigationLayout(
      IReadOnlyList<NavigationPlacement> Ungrouped, IReadOnlyList<NavigationGroup> Groups) {
    public static NavigationLayout None { get; } = new([], []);

    public bool IsEmpty => Ungrouped.Count == 0 && Groups.Count == 0;
  }

  // A port, so the shell's rules can be tested without a server.
  //
  // The arrangement is the deployment's to decide, not this build's: which heading a screen sits
  // under, in what order, under what label, and whether it is offered at all. That answer has to be
  // the same for every client — including ones built at a different time — which is why it comes
  // over the wire rather than out of a constant in here.
  public interface INavigationLayoutService {
    // NavigationLayout.None until the first fetch returns.
    NavigationLayout Current { get; }

    Task RefreshAsync(CancellationToken cancellationToken);

    event EventHandler? Changed;
  }

  // Separate from IPermissionService because they answer different questions of different things.
  // Permissions are about the account and change when a role does; the layout is about the
  // deployment and is the same for everyone. They are fetched at the same moment only because the
  // pane needs both.
  //
  // Nothing here is a security boundary. The server refuses a route on its own, whatever this
  // client was told to draw — the disabled row is a courtesy, not a lock.
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

    // Failure leaves the previous answer standing — NavigationLayout.None on a first attempt that
    // fails — and the shell then falls back to the arrangement compiled into this build. An
    // unreachable server costs the deployment's customisations, not the whole navigation pane.
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
