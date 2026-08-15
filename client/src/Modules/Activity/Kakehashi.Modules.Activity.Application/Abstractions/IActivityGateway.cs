using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Abstractions;

/// <summary>
/// The activity module's port to the server; the gRPC adapter is supplied by the UI layer at
/// composition time.
/// </summary>
/// <remarks>
/// <see cref="RecordAsync"/> carries no facts the server can compute itself: the server decides
/// whose feed, when, and from which device, and refuses any kind outside its own allow-list. The
/// two reportable facts — which build is running, which theme is set — are ones no server can
/// observe for itself. See docs/ACTIVITY.md.
/// </remarks>
public interface IActivityGateway
{
    /// <summary>
    /// Lists one page of the signed-in account's entries, newest first.
    /// </summary>
    /// <param name="filter">
    /// The server clamps an out-of-range page size rather than refusing it, so no caller has to
    /// know the limit.
    /// </param>
    Task<Result<ActivityPageDto>> ListAsync(
        ActivityFeedFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Reports one fact about this client so the account's other devices can see it.
    /// </summary>
    /// <remarks>
    /// Failures come back as <see cref="Result"/>; callers log and carry on — recording is
    /// fire-and-forget, and no user is waiting on it.
    /// </remarks>
    Task<Result> RecordAsync(ClientActivityKind kind, CancellationToken cancellationToken);
}
