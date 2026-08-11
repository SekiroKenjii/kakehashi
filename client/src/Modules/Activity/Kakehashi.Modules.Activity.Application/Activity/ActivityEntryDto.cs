using System;

namespace Kakehashi.Modules.Activity.Application.Activity {
  // One thing that happened to the account, as the server reports it.
  //
  // Facts, not sentences. The server sends a stable kind and the structured detail; the wording, the
  // icon and the grouping are the view model's, which is what lets this client re-word the feed
  // without a server release.
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
