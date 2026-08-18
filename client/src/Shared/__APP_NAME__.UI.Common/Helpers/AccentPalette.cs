using System;
using System.Globalization;
using Windows.UI;

namespace __ROOT_NAMESPACE__.UI.Common.Helpers;

/// <summary>
/// The seven colours an accent override needs: the accent itself and the six shades Windows
/// derives from it. The shades here are linear blends toward white and black rather than the
/// system's own ramp — close enough that controls read as one family, and honest about being an
/// approximation rather than a reimplementation.
/// </summary>
public readonly record struct AccentShades(
    Color Base,
    Color Light1,
    Color Light2,
    Color Light3,
    Color Dark1,
    Color Dark2,
    Color Dark3);

/// <summary>Parses an accent hex and derives the shade ramp the accent resources expect.</summary>
public static class AccentPalette
{
    /// <summary>
    /// Parses <c>#RRGGBB</c>. Anything else — including the empty string a project without an
    /// accent carries — returns false rather than a wrong colour.
    /// </summary>
    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (hex is not { Length: 7 } || hex[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Color.FromArgb(255, r, g, b);

        return true;
    }

    /// <summary>Derives the six shades from the base accent.</summary>
    public static AccentShades Shades(Color accent)
    {
        return new AccentShades(
            accent,
            Toward(accent, 255, 0.30),
            Toward(accent, 255, 0.50),
            Toward(accent, 255, 0.70),
            Toward(accent, 0, 0.25),
            Toward(accent, 0, 0.45),
            Toward(accent, 0, 0.65));
    }

    private static Color Toward(Color from, byte target, double amount)
    {
        return Color.FromArgb(
            255,
            Blend(from.R, target, amount),
            Blend(from.G, target, amount),
            Blend(from.B, target, amount));
    }

    private static byte Blend(byte from, byte target, double amount)
    {
        return (byte)(from + ((target - from) * amount));
    }
}
