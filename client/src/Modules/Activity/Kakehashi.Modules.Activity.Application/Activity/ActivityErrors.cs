using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity;

/// <summary>
/// The errors this module can return. They live in the application layer because this module has
/// no domain: every one of them describes what the network did.
/// </summary>
public static class ActivityErrors
{
    /// <summary>
    /// The server refused the request because there is no valid session behind it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RequestFailed"/>: the view model must clear what it is showing on
    /// this error, or a page left open across a sign-out keeps displaying the previous account's
    /// devices and addresses.
    /// </remarks>
    public static readonly Error NotSignedIn =
        new("Activity.NotSignedIn", "Sign in to see your activity.");

    /// <summary>The server refused because this account is not assigned the Activity module.</summary>
    public static readonly Error NotAssigned = new(
        "Activity.NotAssigned",
        "Your account is not assigned Activity. Ask an administrator for access.");

    public static readonly Error RequestFailed =
        new("Activity.RequestFailed", "The server could not be reached.");

    /// <summary>The server could not read the position this client asked to continue from.</summary>
    /// <remarks>
    /// Not <see cref="RequestFailed"/>: retrying an unreadable token produces the same answer
    /// forever, so the correct response is to restart the list, not to retry.
    /// </remarks>
    public static readonly Error PageLost = new(
        "Activity.PageLost", "The rest of the list could not be loaded. Refresh to start again.");

    /// <summary>The server would not record what this client reported about itself.</summary>
    /// <remarks>
    /// Only reachable when this client is newer than the server: it sends one of two compiled-in
    /// kinds, and the server keeps its own allow-list of what a client may report. Distinct from
    /// <see cref="RequestFailed"/>, whose message would misdiagnose the failure.
    /// </remarks>
    public static readonly Error ReportRefused = new(
        "Activity.ReportRefused",
        "This version of the app reported something the server does not accept.");
}
