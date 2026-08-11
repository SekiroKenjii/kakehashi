using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.SharedKernel;
using NavigationV1 = Kakehashi.Navigation.V1;

namespace Kakehashi.App.Services {
  // One heading, as the layout screen lists them.
  //
  // IsSystem: ships with the product. Renamable and re-orderable, never deletable — a deployment
  // that deleted the heading its administrative screens live under would have nowhere left to put
  // them.
  public sealed record NavGroupRow(string Id, string Title, int SortOrder, bool IsSystem);

  // One destination, as the layout screen manages it.
  //
  // Title: the override, or empty when the destination uses what the code calls it.
  // DefaultTitle: what the code calls it, shown as the placeholder.
  //
  // IsOrphan: a stored row whose destination this build no longer has. Kept rather than deleted,
  // so a module that comes back comes back where somebody put it — and listed here because this is
  // the only screen where anybody can find out it exists.
  //
  // RequiredPermission: what the code enforces. Read-only: it is declared beside the page and
  // enforced by the route gate, and nothing on this screen can change it. That is what makes the
  // rest safe to edit.
  //
  // DefaultGroup: where the code puts this destination when nothing has moved it. What a "reset to
  // what the product shipped" button needs: the reconcile step writes it once as a seed and
  // deliberately never applies it again, so without this the intended place is unrecoverable once
  // somebody has moved the row.
  public sealed record NavItemRow(
      string Id, string ModuleId, string GroupId, string Title, string Icon,
      string DefaultTitle, string DefaultIcon, int SortOrder, bool IsVisible, bool IsOrphan,
      string RequiredPermission, bool HideWhenDenied,
      string DefaultGroup, int DefaultOrder);

  // A heading as the screen wants it. An empty id means "create this one".
  public sealed record NavGroupSpec(string Id, string Title, int SortOrder);

  // A destination as the screen wants it placed.
  public sealed record NavItemSpec(
      string Id, string GroupId, int SortOrder, string Title, string Icon, bool IsVisible);

  // What an apply actually changed, which is not what was sent.
  //
  // The screen posts its whole arrangement, and most of it is usually already true. Reporting what
  // moved rather than what was submitted is what lets the confirmation say something worth reading.
  public sealed record NavApplyOutcome(
      int GroupsCreated, int GroupsUpdated, int GroupsDeleted, int ItemsChanged) {
    public int Total => GroupsCreated + GroupsUpdated + GroupsDeleted + ItemsChanged;
  }

  // The layout operations behind the navigation screen, as a port.
  //
  // An interface so the view model can be tested without a server: the generated gRPC client returns
  // AsyncUnaryCall, which no substitute constructs cleanly.
  public interface INavigationAdminService {
    Task<Result<IReadOnlyList<NavGroupRow>>> ListGroupsAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<NavItemRow>>> ListItemsAsync(CancellationToken cancellationToken);

    Task<Result<NavGroupRow>> CreateGroupAsync(
        string id, string title, int sortOrder, CancellationToken cancellationToken);

    Task<Result<NavGroupRow>> UpdateGroupAsync(
        string id, string title, int sortOrder, CancellationToken cancellationToken);

    Task<Result> DeleteGroupAsync(string id, CancellationToken cancellationToken);

    Task<Result<NavItemRow>> MoveItemAsync(
        string id, string groupId, int sortOrder, CancellationToken cancellationToken);

    Task<Result<NavItemRow>> UpdateItemAsync(
        string id, string title, string icon, bool isVisible, CancellationToken cancellationToken);

    // Writes a whole arrangement, or writes none of it.
    //
    // What the screen uses. The single-row calls above remain because removing one from the contract
    // would break a client compiled against it, but one gesture on this screen produces several
    // changes at once and a sequence of single-row writes cannot fail halfway without leaving the pane
    // half-rearranged.
    Task<Result<NavApplyOutcome>> ApplyLayoutAsync(
        IReadOnlyList<NavGroupSpec> groups,
        IReadOnlyList<NavItemSpec> items,
        CancellationToken cancellationToken);

    // Removes a stored row left over from a module this build no longer has.
    Task<Result> DeleteItemAsync(string id, CancellationToken cancellationToken);

    // Answers what the pane looks like for a role other than the caller's.
    //
    // It reflects what is saved, not what is staged: the server is answering about the
    // arrangement it holds, and it has not been told about unapplied edits.
    Task<Result<NavigationLayout>> PreviewLayoutAsync(
        string roleId, CancellationToken cancellationToken);
  }

  // The administrator's client for the navigation layout service.
  //
  // Every call needs navigation.manage and the server checks it. The client's own check is a
  // courtesy that keeps a screen off the pane; it is not what stops anybody.
  //
  // Failures arrive as Result rather than exceptions because every one of them is
  // something a person did — a heading name already taken, a system heading, a title too long — and
  // the sentence the server wrote is the one worth showing.
  public sealed class NavigationAdminService : INavigationAdminService {
    private readonly NavigationV1.NavigationAdminService.NavigationAdminServiceClient _client;

    public NavigationAdminService(
        NavigationV1.NavigationAdminService.NavigationAdminServiceClient client) {
      ArgumentNullException.ThrowIfNull(client);
      _client = client;
    }

    public Task<Result<IReadOnlyList<NavGroupRow>>> ListGroupsAsync(CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .ListGroupsAsync(new NavigationV1.ListGroupsRequest(), cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        IReadOnlyList<NavGroupRow> rows = [.. reply.Groups.Select(ToRow)];
        return rows;
      });
    }

    public Task<Result<IReadOnlyList<NavItemRow>>> ListItemsAsync(CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .ListItemsAsync(new NavigationV1.ListItemsRequest(), cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        IReadOnlyList<NavItemRow> rows = [.. reply.Items.Select(ToRow)];
        return rows;
      });
    }

    public Task<Result<NavGroupRow>> CreateGroupAsync(
        string id, string title, int sortOrder, CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .CreateGroupAsync(
                new NavigationV1.CreateGroupRequest { Id = id, Title = title, SortOrder = sortOrder },
                cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        return ToRow(reply.Group);
      });
    }

    public Task<Result<NavGroupRow>> UpdateGroupAsync(
        string id, string title, int sortOrder, CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .UpdateGroupAsync(
                new NavigationV1.UpdateGroupRequest { Id = id, Title = title, SortOrder = sortOrder },
                cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        return ToRow(reply.Group);
      });
    }

    public Task<Result> DeleteGroupAsync(string id, CancellationToken ct) {
      return CallVoidAsync(() => _client
          .DeleteGroupAsync(new NavigationV1.DeleteGroupRequest { Id = id }, cancellationToken: ct)
          .ResponseAsync);
    }

    public Task<Result<NavItemRow>> MoveItemAsync(
        string id, string groupId, int sortOrder, CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .MoveItemAsync(
                new NavigationV1.MoveItemRequest {
                  Id = id,
                  GroupId = groupId,
                  SortOrder = sortOrder,
                },
                cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        return ToRow(reply.Item);
      });
    }

    public Task<Result<NavItemRow>> UpdateItemAsync(
        string id, string title, string icon, bool isVisible, CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .UpdateItemAsync(
                new NavigationV1.UpdateItemRequest {
                  Id = id,
                  Title = title,
                  Icon = icon,
                  IsVisible = isVisible,
                },
                cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        return ToRow(reply.Item);
      });
    }

    public Task<Result<NavApplyOutcome>> ApplyLayoutAsync(
        IReadOnlyList<NavGroupSpec> groups,
        IReadOnlyList<NavItemSpec> items,
        CancellationToken ct) {
      ArgumentNullException.ThrowIfNull(groups);
      ArgumentNullException.ThrowIfNull(items);

      return CallAsync(async () => {
        var request = new NavigationV1.ApplyLayoutRequest();
        foreach (var group in groups) {
          request.Groups.Add(new NavigationV1.LayoutGroupSpec {
            Id = group.Id,
            Title = group.Title,
            SortOrder = group.SortOrder,
          });
        }
        foreach (var item in items) {
          request.Items.Add(new NavigationV1.LayoutItemSpec {
            Id = item.Id,
            GroupId = item.GroupId,
            SortOrder = item.SortOrder,
            Title = item.Title,
            Icon = item.Icon,
            IsVisible = item.IsVisible,
          });
        }

        var reply = await _client
            .ApplyLayoutAsync(request, cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        return new NavApplyOutcome(
            reply.GroupsCreated, reply.GroupsUpdated, reply.GroupsDeleted, reply.ItemsChanged);
      });
    }

    public Task<Result> DeleteItemAsync(string id, CancellationToken ct) {
      return CallVoidAsync(() => _client
          .DeleteItemAsync(new NavigationV1.DeleteItemRequest { Id = id }, cancellationToken: ct)
          .ResponseAsync);
    }

    public Task<Result<NavigationLayout>> PreviewLayoutAsync(string roleId, CancellationToken ct) {
      return CallAsync(async () => {
        var reply = await _client
            .PreviewLayoutAsync(
                new NavigationV1.PreviewLayoutRequest { RoleId = roleId }, cancellationToken: ct)
            .ResponseAsync.ConfigureAwait(false);

        return new NavigationLayout(
            [.. reply.Ungrouped.Select(ToPlacement)],
            [.. reply.Groups.Select(group => new NavigationGroup(
                group.Title, [.. group.Items.Select(ToPlacement)]))]);
      });
    }

    // The same mapping the pane's own read does.
    //
    // Duplicated from NavigationLayoutService rather than shared, and only just worth it: that one
    // maps the read every client makes and this one maps a preview only an administrator asks for. If
    // a third caller appears, the mapping moves.
    private static NavigationPlacement ToPlacement(NavigationV1.Item item) {
      return new NavigationPlacement(item.Id, item.Title, item.Icon, item.Enabled);
    }

    private static NavGroupRow ToRow(NavigationV1.Group group) {
      return new NavGroupRow(group.Id, group.Title, group.SortOrder, group.IsSystem);
    }

    private static NavItemRow ToRow(NavigationV1.ItemConfig item) {
      return new NavItemRow(
          item.Id, item.ModuleId, item.GroupId, item.Title, item.Icon,
          item.DefaultTitle, item.DefaultIcon, item.SortOrder, item.IsVisible, item.IsOrphan,
          item.RequiredPermission, item.HideWhenDenied,
          item.DefaultGroup, item.DefaultOrder);
    }

    private static async Task<Result<T>> CallAsync<T>(Func<Task<T>> call) {
      try {
        return Result.Success(await call().ConfigureAwait(false));
      } catch (RpcException exception) {
        return Result.Failure<T>(ToError(exception));
      }
    }

    // The same, for a call whose reply carries nothing worth returning.
    private static async Task<Result> CallVoidAsync<T>(Func<Task<T>> call) {
      try {
        await call().ConfigureAwait(false);
        return Result.Success();
      } catch (RpcException exception) {
        return Result.Failure(ToError(exception));
      }
    }

    // Turns a status into an error carrying the server's own sentence.
    //
    // The server writes these to be read by a person — "Administration is one of the headings this
    // product ships, so it cannot be deleted" — so restating them here would only make them worse.
    private static Error ToError(RpcException exception) {
      var detail = exception.Status.Detail ?? string.Empty;

      // A refusal from the route gate never carries the server's words: that middleware runs before
      // Connect sees the request and answers with a plain HTTP 403, so the client is handed the
      // transport's own "Bad gRPC response. HTTP status code: 403".
      if (detail.Length == 0
          || detail.StartsWith("Bad gRPC response", StringComparison.Ordinal)) {
        detail = exception.StatusCode switch {
          StatusCode.PermissionDenied =>
              "You no longer have permission to arrange the navigation. Ask an administrator to "
                  + "restore it.",
          StatusCode.Unauthenticated => "Your session has ended. Sign in again.",
          StatusCode.Unavailable => "The server could not be reached.",
          _ => "The server could not complete that request.",
        };
      }

      return new Error(exception.StatusCode.ToString(), detail);
    }
  }
}
