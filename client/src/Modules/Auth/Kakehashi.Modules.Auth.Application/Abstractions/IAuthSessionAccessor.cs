using System;
using Kakehashi.Modules.Auth.Domain;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  /// <summary>
  /// Holds the current <see cref="AuthSession"/> for the lifetime of the app. Registered as a
  /// singleton: the access-token provider reads from it, and the use cases update it.
  /// </summary>
  public interface IAuthSessionAccessor {
    /// <summary>The current session, or <see langword="null"/> when no user is signed in.</summary>
    AuthSession? Current { get; }

    /// <summary>
    /// When the current session was established in this app run (interactive sign-in or silent
    /// restore), or <see langword="null"/> when no user is signed in.
    /// </summary>
    DateTimeOffset? SignedInAtUtc { get; }

    void Set(AuthSession session);

    void Clear();
  }
}
