using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Domain {
  // The domain errors the Auth module can return.
  public static class AuthErrors {
    public static readonly Error AccessTokenRequired =
        new("Auth.Session.AccessTokenRequired", "An access token is required to create a session.");

    public static readonly Error NotConfigured =
        new("Auth.NotConfigured", "Authentication is not configured.");

    public static readonly Error LoginFailed =
        new("Auth.Login.Failed", "Interactive sign-in did not complete successfully.");

    public static readonly Error LoginCancelled =
        new("Auth.Login.Cancelled", "Sign-in was cancelled before it completed.");

    public static readonly Error LoginTimedOut =
        new("Auth.Login.TimedOut", "Sign-in timed out. The browser callback was not received.");

    public static readonly Error NoStoredSession =
        new("Auth.Restore.NoStoredSession", "There is no stored session to restore.");

    public static readonly Error RefreshFailed =
        new("Auth.Refresh.Failed", "The session could not be refreshed; sign-in is required.");

    public static readonly Error NotSignedIn =
        new("Auth.NotSignedIn", "There is no signed-in user.");

    public static readonly Error AccountRequestFailed =
        new("Auth.Account.RequestFailed", "The authorization server could not be reached.");
  }
}
