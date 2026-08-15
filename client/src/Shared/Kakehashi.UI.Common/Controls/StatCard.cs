namespace Kakehashi.UI.Common.Controls;

/// <summary>What a stat card's number means, which is what decides its colour.</summary>
/// <remarks>
/// Named for the meaning rather than for any screen's rows, so the palette stays one decision:
/// every screen maps its own rows onto these five meanings instead of adding a parallel enum.
/// </remarks>
public enum StatKind
{
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
