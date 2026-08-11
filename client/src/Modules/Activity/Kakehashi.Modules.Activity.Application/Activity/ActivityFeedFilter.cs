using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// What to ask the feed for. Every default means "do not narrow by this".
  /// </summary>
  /// <remarks>
  /// A record of its own rather than a parameter list, because the view model holds one as the state
  /// of its filter bar and hands the same value to the query, to the next page and to the export. Six
  /// positional arguments threaded through four call sites is how a range ends up swapped with a
  /// search.
  /// </remarks>
  public sealed record ActivityFeedFilter {
    /// <summary>The default ask: the newest page, unfiltered.</summary>
    public static ActivityFeedFilter Default { get; } = new();

    /// <summary>Only entries at or after this moment. Null is unbounded.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only entries at or before this moment. Null is unbounded.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>One <see cref="ActivityCategories"/> value. Empty is every category.</summary>
    public string Category { get; init; } = ActivityCategories.All;

    /// <summary>A substring of the kind, the device or the address. Empty matches everything.</summary>
    public string Search { get; init; } = string.Empty;

    /// <summary>
    /// Where to continue from, as returned by the previous page. Empty starts at the newest entry.
    /// </summary>
    /// <remarks>
    /// Opaque. It is the server's own encoding of a position, and reading or composing one here would
    /// make its shape this client's business forever.
    /// </remarks>
    public string PageToken { get; init; } = string.Empty;

    /// <summary>How many entries to ask for. The server clamps anything unreasonable.</summary>
    public int PageSize { get; init; } = 50;
  }
}
