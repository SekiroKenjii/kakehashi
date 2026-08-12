using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Abstractions {
  // RecordAsync reverses an earlier rule that this port would never carry a write, on the grounds
  // that a history a client can write proves nothing. This one cannot claim anything: it picks from
  // a two-value enum, neither value a security claim, and the server allow-lists what it is handed
  // rather than taking this client's word for it. It cannot say whose feed, cannot say when, and
  // cannot write prose. The two facts it may report — which build is running, which theme is set —
  // are ones no server can observe for itself, so they arrive this way or not at all.
  public interface IActivityGateway {
    // Newest first, for the account behind the token rather than one the filter names. The server
    // clamps an unreasonable page size rather than refusing it, so no caller has to know the limit.
    Task<Result<ActivityPageDto>> ListAsync(
        ActivityFeedFilter filter, CancellationToken cancellationToken);

    // Failure is a Result the caller logs and carries on from: nobody asked for this write, so
    // nobody is waiting to be told it did not happen.
    Task<Result> RecordAsync(ClientActivityKind kind, CancellationToken cancellationToken);
  }
}
