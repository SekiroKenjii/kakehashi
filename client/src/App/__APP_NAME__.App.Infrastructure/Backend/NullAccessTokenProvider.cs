using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Security;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Backend;

/// <summary>
/// The default <see cref="IAccessTokenProvider"/>: always returns no token, so the backend client
/// works without authentication. The Auth module replaces this registration with a session-backed
/// provider.
/// </summary>
public sealed class NullAccessTokenProvider : IAccessTokenProvider
{
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<string?>(null);
    }
}
