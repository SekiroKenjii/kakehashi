using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>
  /// What to ask the feed for. Every default means "do not narrow by this".
  /// </summary>
  public sealed record ActivityFeedFilter {
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
    /// Opaque: the server's own encoding of a position. Never parse or compose one on the client.
    /// </remarks>
    public string PageToken { get; init; } = string.Empty;

    /// <summary>How many entries to ask for. The server clamps anything unreasonable.</summary>
    public int PageSize { get; init; } = 50;
  }
}
