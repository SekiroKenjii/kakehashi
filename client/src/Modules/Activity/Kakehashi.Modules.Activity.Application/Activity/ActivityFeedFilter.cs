using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  // What to ask the feed for. Every default means "do not narrow by this".
  //
  // A record of its own rather than a parameter list, because the view model holds one as the state
  // of its filter bar and hands the same value to the query, to the next page and to the export. Six
  // positional arguments threaded through four call sites is how a range ends up swapped with a
  // search.
  public sealed record ActivityFeedFilter {
    // The default ask: the newest page, unfiltered.
    public static ActivityFeedFilter Default { get; } = new();

    // Only entries at or after this moment. Null is unbounded.
    public DateTimeOffset? From { get; init; }

    // Only entries at or before this moment. Null is unbounded.
    public DateTimeOffset? To { get; init; }

    // One ActivityCategories value. Empty is every category.
    public string Category { get; init; } = ActivityCategories.All;

    // A substring of the kind, the device or the address. Empty matches everything.
    public string Search { get; init; } = string.Empty;

    // Where to continue from, as returned by the previous page. Empty starts at the newest entry.
    //
    // Opaque. It is the server's own encoding of a position, and reading or composing one here would
    // make its shape this client's business forever.
    public string PageToken { get; init; } = string.Empty;

    // How many entries to ask for. The server clamps anything unreasonable.
    public int PageSize { get; init; } = 50;
  }
}
