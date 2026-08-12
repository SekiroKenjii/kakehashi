using System.Collections.Generic;

namespace Kakehashi.Modules.Activity.Application.Activity {
  public sealed record ActivityPageDto(
      IReadOnlyList<ActivityEntryDto> Entries,
      string NextPageToken,
      int Total,
      IReadOnlyDictionary<string, int> Counts,
      IReadOnlyDictionary<string, int> KindCounts,
      int RetentionDays) {
    public bool HasMore => NextPageToken.Length > 0;

    // Keyed by ActivityCategories. Zero for a category the server did not mention, which is the
    // honest answer: it counts what it has, and a category with nothing in it never appears.
    public int CountIn(string category) {
      return Counts.TryGetValue(category, out int count) ? count : 0;
    }

    // Not derivable from CountIn: Security holds refused sign-ins and password changes together,
    // and "one sign-in was refused this week" is a different sentence from "three security things
    // happened". Counting the loaded page instead would give a number that changed as somebody
    // scrolled.
    public int CountOf(string kind) {
      return KindCounts.TryGetValue(kind, out int count) ? count : 0;
    }
  }
}
