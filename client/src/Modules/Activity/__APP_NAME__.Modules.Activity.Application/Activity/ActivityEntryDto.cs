using System;

namespace __ROOT_NAMESPACE__.Modules.Activity.Application.Activity;

/// <summary>One thing that happened to the account, as the server reports it.</summary>
/// <remarks>
/// The server sends a stable kind and structured detail; wording, icon and grouping belong to
/// the view model, so the feed can be re-worded without a server release.
/// </remarks>
public sealed record ActivityEntryDto(
    string Id,
    string Kind,
    string Category,
    string SessionId,
    string Device,
    string Platform,
    string IPAddress,
    DateTimeOffset OccurredAt);
