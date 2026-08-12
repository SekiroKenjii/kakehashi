namespace Kakehashi.UI.Common.Controls {
  // Named for the meaning, not for a screen's rows. These were Total/Active/Inactive/Idle, which
  // described the Users screen and nothing else: a second screen cannot call its failed sign-ins
  // "inactive", so it would have had to invent a parallel enum and a parallel palette.
  public enum StatKind {
    // A plain count, no judgement.
    Accent,

    Positive,

    // Dormant, which is not the same as wrong.
    Muted,

    Warning,

    Critical,
  }

  public sealed record StatCard(
      string Label, string Value, string Detail, string Glyph, StatKind Kind);
}
