using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>One thing that happened to the signed-in account, on any of their devices.</summary>
  /// <param name="Kind">
  /// A stable label such as <c>SignedIn</c>. The view model switches on it to choose wording and an
  /// icon; an unrecognised value is shown as-is rather than dropped, because a feed that silently
  /// hides what it does not understand is worse than one that shows a raw string.
  /// </param>
  /// <param name="Device">
  /// Whatever the user agent claimed. Untrusted, often empty, and shown only as a hint — it is what
  /// answers the reader's actual question, which is "was that me?".
  /// </param>
  /// <param name="OccurredAt">When it happened, not when it was fetched.</param>
  public sealed record ActivityEntryDto(
      string Kind, string Device, string IPAddress, DateTimeOffset OccurredAt);
}
