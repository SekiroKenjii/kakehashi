using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.SharedKernel;
using NavigationV1 = Kakehashi.Navigation.V1;

namespace Kakehashi.App.Services;

/// <param name="IsSystem">
/// Ships with the product: renamable and re-orderable, never deletable — the administrative
/// screens must always have a heading to live under.
/// </param>
public sealed record NavGroupRow(string Id, string Title, int SortOrder, bool IsSystem);

/// <param name="Title">The override, or empty when the destination uses what the code calls it.</param>
/// <param name="DefaultTitle">What the code calls it, shown as the placeholder.</param>
/// <param name="IsOrphan">
/// A stored row whose destination is not part of this build. Kept rather than deleted so a
/// returning module keeps its place; this screen is the only place such a row is visible.
/// </param>
/// <param name="RequiredPermission">
/// What the code enforces. Read-only: declared beside the page and enforced by the route gate;
/// nothing on this screen can change it.
/// </param>
/// <param name="DefaultGroup">
/// Where the code puts this destination when nothing has moved it. The reconcile step writes it
/// once as a seed and never applies it again, so this field is the only record of the shipped
/// placement — what a "reset" needs.
/// </param>
public sealed record NavItemRow(
    string Id, string ModuleId, string GroupId, string Title, string Icon,
    string DefaultTitle, string DefaultIcon, int SortOrder, bool IsVisible, bool IsOrphan,
    string RequiredPermission, bool HideWhenDenied,
    string DefaultGroup, int DefaultOrder);

/// <summary>A heading to apply; an empty id means "create this one".</summary>
public sealed record NavGroupSpec(string Id, string Title, int SortOrder);

public sealed record NavItemSpec(
    string Id, string GroupId, int SortOrder, string Title, string Icon, bool IsVisible);

/// <summary>The changes an apply actually made, not what was submitted.</summary>
/// <remarks>
/// The screen posts its whole arrangement, most of which is usually already true on the server;
/// the counts report only what moved.
/// </remarks>
public sealed record NavApplyOutcome(
    int GroupsCreated, int GroupsUpdated, int GroupsDeleted, int ItemsChanged)
{
    public int Total => GroupsCreated + GroupsUpdated + GroupsDeleted + ItemsChanged;
}

/// <summary>
/// The layout operations behind the navigation screen, as a port.
/// </summary>
/// <remarks>
/// An interface so the view model can be tested without a server: the generated gRPC client returns
/// <c>AsyncUnaryCall</c>, which no substitute constructs cleanly.
/// </remarks>
public interface INavigationAdminService
{
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

    /// <summary>
    /// Writes a whole arrangement, or writes none of it.
    /// </summary>
    /// <remarks>
    /// What the screen uses. The single-row calls above remain because removing one from the contract
    /// would break a client compiled against it, but one gesture on this screen produces several
    /// changes at once and a sequence of single-row writes cannot fail halfway without leaving the pane
    /// half-rearranged.
    /// </remarks>
    Task<Result<NavApplyOutcome>> ApplyLayoutAsync(
        IReadOnlyList<NavGroupSpec> groups,
        IReadOnlyList<NavItemSpec> items,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a stored row left over from a module that is not part of this build.
    /// </summary>
    Task<Result> DeleteItemAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Answers what the pane looks like for a role other than the caller's.
    /// </summary>
    /// <remarks>
    /// It reflects what is <em>saved</em>, not what is staged: the server is answering about the
    /// arrangement it holds, and it has not been told about unapplied edits.
    /// </remarks>
    Task<Result<NavigationLayout>> PreviewLayoutAsync(
        string roleId, CancellationToken cancellationToken);
}

/// <summary>The administrator's client for the navigation layout service.</summary>
/// <remarks>
/// The server checks <c>navigation.manage</c> on every call; the client's own check only keeps
/// the screen off the pane. Failures arrive as <see cref="Result"/> rather than exceptions: they
/// are expected user-caused refusals, and the server's message is shown verbatim.
/// </remarks>
public sealed class NavigationAdminService : INavigationAdminService
{
    private readonly NavigationV1.NavigationAdminService.NavigationAdminServiceClient _client;

    public NavigationAdminService(
        NavigationV1.NavigationAdminService.NavigationAdminServiceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public Task<Result<IReadOnlyList<NavGroupRow>>> ListGroupsAsync(CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _client
                .ListGroupsAsync(new NavigationV1.ListGroupsRequest(), cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<NavGroupRow> rows = [.. reply.Groups.Select(ToRow)];

            return rows;
        });
    }

    public Task<Result<IReadOnlyList<NavItemRow>>> ListItemsAsync(CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _client
                .ListItemsAsync(new NavigationV1.ListItemsRequest(), cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<NavItemRow> rows = [.. reply.Items.Select(ToRow)];

            return rows;
        });
    }

    public Task<Result<NavGroupRow>> CreateGroupAsync(
        string id, string title, int sortOrder, CancellationToken ct)
    {
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
        string id, string title, int sortOrder, CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _client
                .UpdateGroupAsync(
                    new NavigationV1.UpdateGroupRequest { Id = id, Title = title, SortOrder = sortOrder },
                    cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            return ToRow(reply.Group);
        });
    }

    public Task<Result> DeleteGroupAsync(string id, CancellationToken ct)
    {
        return CallVoidAsync(() => _client
            .DeleteGroupAsync(new NavigationV1.DeleteGroupRequest { Id = id }, cancellationToken: ct)
            .ResponseAsync);
    }

    public Task<Result<NavItemRow>> MoveItemAsync(
        string id, string groupId, int sortOrder, CancellationToken ct)
    {
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
        string id, string title, string icon, bool isVisible, CancellationToken ct)
    {
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
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(items);

        return CallAsync(async () => {
            var request = new NavigationV1.ApplyLayoutRequest();
            foreach (var group in groups)
            {
                request.Groups.Add(new NavigationV1.LayoutGroupSpec {
                    Id = group.Id,
                    Title = group.Title,
                    SortOrder = group.SortOrder,
                });
            }
            foreach (var item in items)
            {
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

    public Task<Result> DeleteItemAsync(string id, CancellationToken ct)
    {
        return CallVoidAsync(() => _client
            .DeleteItemAsync(new NavigationV1.DeleteItemRequest { Id = id }, cancellationToken: ct)
            .ResponseAsync);
    }

    public Task<Result<NavigationLayout>> PreviewLayoutAsync(string roleId, CancellationToken ct)
    {
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

    /// <summary>
    /// The same mapping the pane's own read does.
    /// </summary>
    /// <remarks>
    /// Deliberately duplicated from NavigationLayoutService rather than shared; if a third caller
    /// appears, the mapping moves to one place.
    /// </remarks>
    private static NavigationPlacement ToPlacement(NavigationV1.Item item)
    {
        return new NavigationPlacement(item.Id, item.Title, item.Icon, item.Enabled);
    }

    private static NavGroupRow ToRow(NavigationV1.Group group)
    {
        return new NavGroupRow(group.Id, group.Title, group.SortOrder, group.IsSystem);
    }

    private static NavItemRow ToRow(NavigationV1.ItemConfig item)
    {
        return new NavItemRow(
            item.Id, item.ModuleId, item.GroupId, item.Title, item.Icon,
            item.DefaultTitle, item.DefaultIcon, item.SortOrder, item.IsVisible, item.IsOrphan,
            item.RequiredPermission, item.HideWhenDenied,
            item.DefaultGroup, item.DefaultOrder);
    }

    private static async Task<Result<T>> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return Result.Success(await call().ConfigureAwait(false));
        }
        catch (RpcException exception)
        {
            return Result.Failure<T>(ToError(exception));
        }
    }

    private static async Task<Result> CallVoidAsync<T>(Func<Task<T>> call)
    {
        try
        {
            await call().ConfigureAwait(false);

            return Result.Success();
        }
        catch (RpcException exception)
        {
            return Result.Failure(ToError(exception));
        }
    }

    /// <summary>Turns a status into an error carrying the server's own sentence.</summary>
    /// <remarks>
    /// The server writes these to be read by a person — "Administration is one of the headings this
    /// product ships, so it cannot be deleted" — and they are shown verbatim.
    /// </remarks>
    private static Error ToError(RpcException exception)
    {
        var detail = exception.Status.Detail ?? string.Empty;

        // A route-gate refusal never carries the server's words: it answers with a plain HTTP 403
        // before Connect sees the request, so the transport's own text is all that arrives.
        if (detail.Length == 0
            || detail.StartsWith("Bad gRPC response", StringComparison.Ordinal))
        {
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
