using System.Collections.Generic;

namespace Kakehashi.Modules.Activity.Application.Activity;

/// <summary>One page of the feed, plus what the screen around it needs to describe itself.</summary>
public sealed record ActivityPageDto(
    IReadOnlyList<ActivityEntryDto> Entries,
    string NextPageToken,
    int Total,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, int> KindCounts,
    int RetentionDays)
{
    public bool HasMore => NextPageToken.Length > 0;

    /// <summary>
    /// How many entries there are in a category, keyed by <see cref="ActivityCategories"/>.
    /// </summary>
    /// <remarks>
    /// Zero for a category the server did not mention: a category with nothing in it does not
    /// appear in the reply.
    /// </remarks>
    public int CountIn(string category)
    {
        return Counts.TryGetValue(category, out int count) ? count : 0;
    }

    /// <summary>
    /// How many entries there are of one kind.
    /// </summary>
    /// <remarks>
    /// Not derivable from <see cref="CountIn"/> — a category aggregates several kinds — nor from
    /// the loaded page, whose count would change as more pages load.
    /// </remarks>
    public int CountOf(string kind)
    {
        return KindCounts.TryGetValue(kind, out int count) ? count : 0;
    }
}
