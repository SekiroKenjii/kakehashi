namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// The categories the feed can be filtered by, as the server names them.
  /// </summary>
  /// <remarks>
  /// Named here as well as on the server, and the duplication is the module boundary rather than a
  /// failure of it — the same trade the kind strings already make. What it buys is that this client
  /// can add a chip, re-order them or re-label them without a server release, and that a category the
  /// server invents later still arrives, still counts, and simply has no chip until somebody adds one.
  /// <para>
  /// <see cref="All"/> is this client's own idea. The server has no such category; an empty category
  /// means "do not narrow", and the chip that says so needs a value to hold.
  /// </para>
  /// </remarks>
  public static class ActivityCategories {
    /// <summary>Every category. Not a server value — the wire carries an empty string for this.</summary>
    public const string All = "";

    /// <summary>Sessions beginning and ending.</summary>
    public const string SignIn = "SignIn";

    /// <summary>Refused attempts, password changes, somebody else ending a session.</summary>
    public const string Security = "Security";

    /// <summary>What happened to the application rather than to the account.</summary>
    public const string System = "System";
  }
}
