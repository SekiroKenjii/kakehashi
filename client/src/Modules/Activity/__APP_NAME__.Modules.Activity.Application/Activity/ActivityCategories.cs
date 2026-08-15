namespace __ROOT_NAMESPACE__.Modules.Activity.Application.Activity;

/// <summary>
/// The categories the feed can be filtered by, as the server names them.
/// </summary>
/// <remarks>
/// These strings cross the wire as the feed filter's category and must match the server's
/// spelling exactly. A category the server adds later still arrives and still counts — it simply
/// has no chip until one is added here. <see cref="All"/> is client-only: the wire carries an
/// empty string, meaning "do not narrow".
/// </remarks>
public static class ActivityCategories
{
    /// <summary>Every category. Not a server value — the wire carries an empty string for this.</summary>
    public const string All = "";

    /// <summary>Sessions beginning and ending.</summary>
    public const string SignIn = "SignIn";

    /// <summary>Refused attempts, password changes, somebody else ending a session.</summary>
    public const string Security = "Security";

    /// <summary>What happened to the application rather than to the account.</summary>
    public const string System = "System";
}
