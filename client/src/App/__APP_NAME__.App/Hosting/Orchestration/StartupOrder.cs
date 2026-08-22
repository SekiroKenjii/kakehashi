namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>
/// The order of every startup step, spelled once.
/// </summary>
/// <remarks>
/// The numbers are a coordinate system rather than a sequence: each one is pinned by what it must
/// run after and what must run after it, and the gaps are room for steps nobody has written yet.
/// A new step is placed by reading its neighbours here, which is why they are collected in one
/// file instead of being a literal on each orchestrator.
/// </remarks>
internal static class StartupOrder
{
    /// <summary>
    /// Before every window: the framework binds its accent brushes the first time one draws, and
    /// reads only the colours in place by then.
    /// </summary>
    public const int Accent = 5;

    /// <summary>The splash appears here, so it covers everything below it.</summary>
    public const int Splash = 10;

    /// <summary>Produces the token every step below needs.</summary>
    public const int Authentication = 15;

    /// <summary>
    /// After authentication because both its calls need a token, before the shell because the
    /// navigation pane is drawn wrong from only one of the two answers.
    /// </summary>
    public const int Permission = 17;

    /// <summary>Creates the main window's content, which the steps below reach into.</summary>
    public const int Shell = 20;

    /// <summary>After the shell: the theme is applied to main-window content.</summary>
    public const int Theme = 30;

    /// <summary>Last, so the splash stays up until the main window is ready.</summary>
    public const int Activation = 40;
}
