using System.Collections.Generic;

namespace Kakehashi.Modules.Activity.Application.Activity {
  // One page of the feed, plus what the screen around it needs to describe itself.
  public sealed record ActivityPageDto(
      IReadOnlyList<ActivityEntryDto> Entries,
      string NextPageToken,
      int Total,
      IReadOnlyDictionary<string, int> Counts,
      IReadOnlyDictionary<string, int> KindCounts,
      int RetentionDays) {
    // Whether there is another page to ask for.
    public bool HasMore => NextPageToken.Length > 0;

    // How many entries there are in a category, keyed by ActivityCategories.
    //
    // Zero for a category the server did not mention, which is the honest answer: the server counts
    // what it has, and a category with nothing in it does not appear in the reply.
    public int CountIn(string category) {
      return Counts.TryGetValue(category, out int count) ? count : 0;
    }

    // How many entries there are of one kind, for a card that states one exact fact.
    //
    // Not derivable from CountIn: Security holds refused sign-ins and password changes
    // together, and "one sign-in was refused this week" is a different sentence from "three security
    // things happened". Counting the loaded page instead would give a number that changed as somebody
    // scrolled.
    public int CountOf(string kind) {
      return KindCounts.TryGetValue(kind, out int count) ? count : 0;
    }
  }
}
