using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  // Facts, not sentences: the server sends a stable kind and structured detail, and the wording,
  // the icon and the grouping are the view model's — which is what lets this client re-word the
  // feed without a server release.
  public sealed record ActivityEntryDto(
      string Id,
      string Kind,
      string Category,
      string SessionId,
      string Device,
      string Platform,
      string IPAddress,
      DateTimeOffset OccurredAt);
}
