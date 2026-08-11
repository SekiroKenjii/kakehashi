using System.Collections.Generic;

namespace Kakehashi.Modules.Activity.Application.Activity {
  /// <summary>One page of the feed, plus what the screen around it needs to describe itself.</summary>
  public sealed record ActivityPageDto(
      IReadOnlyList<ActivityEntryDto> Entries,
      string NextPageToken,
      int Total,
      IReadOnlyDictionary<string, int> Counts,
      IReadOnlyDictionary<string, int> KindCounts,
      int RetentionDays) {
    /// <summary>Whether there is another page to ask for.</summary>
    public bool HasMore => NextPageToken.Length > 0;

    /// <summary>
    /// How many entries there are in a category, keyed by <see cref="ActivityCategories"/>.
    /// </summary>
    /// <remarks>
    /// Zero for a category the server did not mention, which is the honest answer: the server counts
    /// what it has, and a category with nothing in it does not appear in the reply.
    /// </remarks>
    public int CountIn(string category) {
      return Counts.TryGetValue(category, out int count) ? count : 0;
    }

    /// <summary>
    /// How many entries there are of one kind, for a card that states one exact fact.
    /// </summary>
    /// <remarks>
    /// Not derivable from <see cref="CountIn"/>: Security holds refused sign-ins and password changes
    /// together, and "one sign-in was refused this week" is a different sentence from "three security
    /// things happened". Counting the loaded page instead would give a number that changed as somebody
    /// scrolled.
    /// </remarks>
    public int CountOf(string kind) {
      return KindCounts.TryGetValue(kind, out int count) ? count : 0;
    }
  }
}
