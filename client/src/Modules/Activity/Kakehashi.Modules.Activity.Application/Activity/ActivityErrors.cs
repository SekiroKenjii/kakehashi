using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity {
  // In the application layer rather than a domain one because this module has no domain: nothing
  // here enforces a rule about the world, and every one of these describes what the network did.
  public static class ActivityErrors {
    // Distinct from RequestFailed on purpose. An expired token and an empty feed look identical on
    // screen and mean opposite things, and the view model has to clear what it is showing for this
    // one — otherwise a page left open across a sign-out keeps displaying the previous account's
    // devices and addresses.
    public static readonly Error NotSignedIn =
        new("Activity.NotSignedIn", "Sign in to see your activity.");

    public static readonly Error NotAssigned = new(
        "Activity.NotAssigned",
        "Your account is not assigned Activity. Ask an administrator for access.");

    public static readonly Error RequestFailed =
        new("Activity.RequestFailed", "The server could not be reached.");

    // Start the list again rather than retry, which is why it is not RequestFailed: retrying an
    // unreadable page token produces the same answer forever, and a "load more" button that never
    // works is worse than a list that visibly restarted.
    public static readonly Error PageLost = new(
        "Activity.PageLost", "The rest of the list could not be loaded. Refresh to start again.");

    // Only reachable when this client is newer than the server: it sends one of two compiled-in
    // kinds, and the server keeps its own allow-list of what a client may report. Kept distinct
    // from RequestFailed because "the server could not be reached" would be false, and a wrong
    // diagnosis is worse than a vague one.
    public static readonly Error ReportRefused = new(
        "Activity.ReportRefused",
        "This version of the app reported something the server does not accept.");
  }
}
