using System;
using Kakehashi.Modules.Auth.Domain;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // Registered as a singleton: the access-token provider reads it, the use cases write it.
  public interface IAuthSessionAccessor {
    AuthSession? Current { get; }

    // When the session was established in this app run; a silent restore resets it, so it is not
    // the server-side sign-in time.
    DateTimeOffset? SignedInAtUtc { get; }

    void Set(AuthSession session);

    void Clear();
  }
}
