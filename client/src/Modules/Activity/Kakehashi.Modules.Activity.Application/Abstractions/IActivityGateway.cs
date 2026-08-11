using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Abstractions {
  // The activity module's port to the server. The application layer declares the contract; the
  // concrete adapter — which knows about gRPC, generated types and network failures — is supplied
  // by the UI layer at composition time.
  //
  // This said "one method, and there will never be a second write one… a history a client can write
  // is a history that proves nothing", and RecordAsync is that second method, so the
  // claim deserves an answer rather than a deletion.
  //
  // What makes a history worthless is a client that gets to say what happened. This one does not: it
  // picks from a two-value enum, neither of which is a security claim, and the server refuses
  // anything outside its own allow-list without taking this client's word for it. It cannot say
  // whose feed, cannot say when, and cannot write prose. The two facts it may report — which build
  // is running, which theme is set — are ones no server can observe for itself, so they arrive this
  // way or the feed simply does not have them.
  public interface IActivityGateway {
    // Lists one page of the signed-in account's entries, newest first.
    //
    // filter: what to ask for. The server clamps an unreasonable page size rather than refusing
    // it, so no caller has to know the limit.
    Task<Result<ActivityPageDto>> ListAsync(
        ActivityFeedFilter filter, CancellationToken cancellationToken);

    // Reports one fact about this client so the account's other devices can see it.
    //
    // A failure is a Result like any other, and the caller's usual answer is to log it
    // and carry on: nobody asked for this to happen, so nobody is waiting to be told it did not.
    Task<Result> RecordAsync(ClientActivityKind kind, CancellationToken cancellationToken);
  }
}
