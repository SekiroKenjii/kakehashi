using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// The errors this module can return.
  /// </summary>
  /// <remarks>
  /// They live in the application layer rather than a domain one because this module has no domain:
  /// nothing here enforces a rule about the world, and these two describe what the network did.
  /// </remarks>
  public static class ActivityErrors {
    /// <summary>
    /// The server refused the request because there is no valid session behind it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RequestFailed"/> on purpose. An expired token and an empty feed
    /// look identical on screen and mean opposite things, and the view model has to clear what it
    /// is showing for this one — otherwise a page left open across a sign-out keeps displaying the
    /// previous account's devices and addresses.
    /// </remarks>
    public static readonly Error NotSignedIn =
        new("Activity.NotSignedIn", "Sign in to see your activity.");

    /// <summary>The server refused because this account is not assigned the Activity module.</summary>
    public static readonly Error NotAssigned = new(
        "Activity.NotAssigned",
        "Your account is not assigned Activity. Ask an administrator for access.");

    public static readonly Error RequestFailed =
        new("Activity.RequestFailed", "The server could not be reached.");
  }
}
