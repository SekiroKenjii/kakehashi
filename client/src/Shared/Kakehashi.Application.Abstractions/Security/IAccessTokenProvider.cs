using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Security {
  /// <summary>
  /// Supplies the bearer access token for outbound calls to the backend. The host's backend client
  /// asks for a token per request and, when one is returned, adds it as an
  /// <c>Authorization: Bearer</c> header; a <see langword="null"/> result means "no token" and the
  /// request is sent unauthenticated.
  /// </summary>
  /// <remarks>
  /// The host registers a no-op implementation by default, so the backend works with or without
  /// authentication. The Auth module replaces it with a session-backed provider that transparently
  /// refreshes the token when it is close to expiry. Defining the contract here (rather than in the
  /// host or a module) lets both the host infrastructure and a feature module depend on it without a
  /// project reference between them.
  /// </remarks>
  public interface IAccessTokenProvider {
    /// <summary>Returns the current access token, or <see langword="null"/> when none is available.</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
  }
}
