using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  // Every default means "do not narrow by this".
  //
  // A record of its own rather than a parameter list, because the view model holds one as the state
  // of its filter bar and hands the same value to the query, to the next page and to the export.
  // Six positional arguments threaded through four call sites is how a range ends up swapped with
  // a search.
  public sealed record ActivityFeedFilter {
    public static ActivityFeedFilter Default { get; } = new();

    // Inclusive; null is unbounded.
    public DateTimeOffset? From { get; init; }

    // Inclusive; null is unbounded.
    public DateTimeOffset? To { get; init; }

    // An ActivityCategories value; empty is every category.
    public string Category { get; init; } = ActivityCategories.All;

    // Matched against the kind, the device and the address; empty matches everything.
    public string Search { get; init; } = string.Empty;

    // As returned by the previous page; empty starts at the newest entry. Opaque — it is the
    // server's own encoding of a position, and reading or composing one here would make its shape
    // this client's business forever.
    public string PageToken { get; init; } = string.Empty;

    // The server clamps anything unreasonable.
    public int PageSize { get; init; } = 50;
  }
}
