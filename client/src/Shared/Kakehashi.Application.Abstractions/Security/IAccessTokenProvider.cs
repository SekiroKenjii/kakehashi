using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Security {
  // Null means no token, and the backend client sends the request unauthenticated rather than
  // failing. The host registers a no-op implementation by default so the backend works without
  // authentication; the Auth module swaps in a session-backed provider that refreshes near expiry.
  // The contract lives here, not in the host or the module, so both can depend on it without a
  // project reference between them.
  public interface IAccessTokenProvider {
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
  }
}
