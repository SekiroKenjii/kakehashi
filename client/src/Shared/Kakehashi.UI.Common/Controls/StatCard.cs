namespace Kakehashi.UI.Common.Controls {
  /// <summary>What a stat card's number means, which is what decides its colour.</summary>
  /// <remarks>
  /// Named for the meaning rather than for a screen's rows. The first four of these were
  /// Total/Active/Inactive/Idle, which described the Users screen and nothing else — a second screen
  /// cannot call its failed sign-ins "inactive", so it would have had to invent a parallel enum and
  /// a parallel palette. Naming the meaning keeps the palette one decision for every screen.
  /// </remarks>
  public enum StatKind {
    /// <summary>No judgement, just a count. Takes the app accent.</summary>
    Accent,

    /// <summary>Healthy.</summary>
    Positive,

    /// <summary>Dormant, which is not the same as wrong.</summary>
    Muted,

    /// <summary>Worth a look.</summary>
    Warning,

    /// <summary>Wrong — usually the reason somebody opened the screen.</summary>
    Critical,
  }

  /// <summary>One of the counts along the top of a screen.</summary>
  public sealed record StatCard(
      string Label, string Value, string Detail, string Glyph, StatKind Kind);
}
