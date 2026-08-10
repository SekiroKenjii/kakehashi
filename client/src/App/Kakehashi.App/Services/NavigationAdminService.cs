using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.SharedKernel;
using NavigationV1 = Kakehashi.Navigation.V1;

namespace Kakehashi.App.Services {
  /// <summary>One heading, as the layout screen lists them.</summary>
  /// <param name="IsSystem">
  /// Ships with the product. Renamable and re-orderable, never deletable — a deployment that deleted
  /// the heading its administrative screens live under would have nowhere left to put them.
  /// </param>
  public sealed record NavGroupRow(string Id, string Title, int SortOrder, bool IsSystem);

  /// <summary>One destination, as the layout screen manages it.</summary>
  /// <param name="Title">The override, or empty when the destination uses what the code calls it.</param>
  /// <param name="DefaultTitle">What the code calls it, shown as the placeholder.</param>
  /// <param name="IsOrphan">
  /// A stored row whose destination this build no longer has. Kept rather than deleted, so a module
  /// that comes back comes back where somebody put it — and listed here because this is the only
  /// screen where anybody can find out it exists.
  /// </param>
  /// <param name="RequiredPermission">
  /// What the code enforces. Read-only: it is declared beside the page and enforced by the route
  /// gate, and nothing on this screen can change it. That is what makes the rest safe to edit.
  /// </param>
  public sealed record NavItemRow(
      string Id, string ModuleId, string GroupId, string Title, string Icon,
      string DefaultTitle, string DefaultIcon, int SortOrder, bool IsVisible, bool IsOrphan,
      string RequiredPermission, bool HideWhenDenied);

  /// <summary>
  /// The layout operations behind the navigation screen, as a port.
  /// </summary>
  /// <remarks>
  /// An interface so the view model can be tested without a server: the generated gRPC client returns
  /// <c>AsyncUnaryCall</c>, which no substitute constructs cleanly.
  /// </remarks>
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
  }

  /// <summary>The administrator's client for the navigation layout service.</summary>
  /// <remarks>
  /// Every call needs <c>navigation.manage</c> and the server checks it. The client's own check is a
  /// courtesy that keeps a screen off the pane; it is not what stops anybody.
  /// <para>
  /// Failures arrive as <see cref="Result"/> rather than exceptions because every one of them is
  /// something a person did — a heading name already taken, a system heading, a title too long — and
  /// the sentence the server wrote is the one worth showing.
  /// </para>
  /// </remarks>
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

    private static NavGroupRow ToRow(NavigationV1.Group group) {
      return new NavGroupRow(group.Id, group.Title, group.SortOrder, group.IsSystem);
    }

    private static NavItemRow ToRow(NavigationV1.ItemConfig item) {
      return new NavItemRow(
          item.Id, item.ModuleId, item.GroupId, item.Title, item.Icon,
          item.DefaultTitle, item.DefaultIcon, item.SortOrder, item.IsVisible, item.IsOrphan,
          item.RequiredPermission, item.HideWhenDenied);
    }

    private static async Task<Result<T>> CallAsync<T>(Func<Task<T>> call) {
      try {
        return Result.Success(await call().ConfigureAwait(false));
      } catch (RpcException exception) {
        return Result.Failure<T>(ToError(exception));
      }
    }

    /// <summary>The same, for a call whose reply carries nothing worth returning.</summary>
    private static async Task<Result> CallVoidAsync<T>(Func<Task<T>> call) {
      try {
        await call().ConfigureAwait(false);
        return Result.Success();
      } catch (RpcException exception) {
        return Result.Failure(ToError(exception));
      }
    }

    /// <summary>Turns a status into an error carrying the server's own sentence.</summary>
    /// <remarks>
    /// The server writes these to be read by a person — "Administration is one of the headings this
    /// product ships, so it cannot be deleted" — so restating them here would only make them worse.
    /// </remarks>
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
