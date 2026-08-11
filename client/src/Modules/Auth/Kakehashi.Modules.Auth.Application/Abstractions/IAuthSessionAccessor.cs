using System;
using Kakehashi.Modules.Auth.Domain;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // Holds the current AuthSession for the lifetime of the app. Registered as a
  // singleton: the access-token provider reads from it, and the use cases update it.
  public interface IAuthSessionAccessor {
    // The current session, or null when no user is signed in.
    AuthSession? Current { get; }

    // When the current session was established in this app run (interactive sign-in or silent
    // restore), or null when no user is signed in.
    DateTimeOffset? SignedInAtUtc { get; }

    void Set(AuthSession session);

    void Clear();
  }
}
