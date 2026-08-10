using System.Collections.Generic;
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
  /// One method, and there will never be a second write one. The feed is append-only and only the
  /// server appends: entries are written by the server reacting to facts the modules announce, and
  /// a history a client can write is a history that proves nothing.
  /// </remarks>
  public interface IActivityGateway {
    /// <summary>
    /// Lists the signed-in account's most recent entries, newest first.
    /// </summary>
    /// <param name="take">
    /// A cap on how many to return. The server clamps anything unreasonable rather than refusing
    /// it, so no caller has to know the limit.
    /// </param>
    Task<Result<IReadOnlyList<ActivityEntryDto>>> ListAsync(
        int take, CancellationToken cancellationToken);
  }
}
