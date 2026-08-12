using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Security;

namespace Kakehashi.App.Infrastructure.Backend {
  // The default, so the backend client works without authentication; the Auth module replaces this
  // registration with a session-backed provider.
  public sealed class NullAccessTokenProvider : IAccessTokenProvider {
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) {
      return ValueTask.FromResult<string?>(null);
    }
  }
}
