using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Abstractions {
  /// <summary>
  /// The activity module's port to the server. The application layer declares the contract; the
  /// concrete adapter — which knows about gRPC, generated types and network failures — is supplied
  /// by the UI layer at composition time.
  /// </summary>
  /// <remarks>
  /// <see cref="RecordAsync"/> never lets this client say what happened: the server decides whose
  /// feed, when, and from which device, and refuses any kind outside its own allow-list. The two
  /// reportable facts — which build is running, which theme is set — are ones no server can observe
  /// for itself, so they arrive this way or the feed does not have them. See docs/ACTIVITY.md.
  /// </remarks>
  public interface IActivityGateway {
    /// <summary>
    /// Lists one page of the signed-in account's entries, newest first.
    /// </summary>
    /// <param name="filter">
    /// What to ask for. The server clamps an unreasonable page size rather than refusing it, so no
    /// caller has to know the limit.
    /// </param>
    Task<Result<ActivityPageDto>> ListAsync(
        ActivityFeedFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Reports one fact about this client so the account's other devices can see it.
    /// </summary>
    /// <remarks>
    /// A failure is a <see cref="Result"/> like any other, and the caller's usual answer is to log it
    /// and carry on: nobody asked for this to happen, so nobody is waiting to be told it did not.
    /// </remarks>
    Task<Result> RecordAsync(ClientActivityKind kind, CancellationToken cancellationToken);
  }
}
