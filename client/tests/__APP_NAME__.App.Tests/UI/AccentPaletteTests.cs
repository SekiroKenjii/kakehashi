using __ROOT_NAMESPACE__.UI.Common.Helpers;
using Windows.UI;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.UI;

/// <summary>
/// Unit tests for <see cref="AccentPalette"/>: hex parsing including every refusal, and the shade
/// ramp's two invariants — lights get lighter, darks get darker.
/// </summary>
public sealed class AccentPaletteTests
{
    [Fact]
    public void TryParse_ReadsASixDigitHex()
    {
        Assert.True(AccentPalette.TryParse("#C4513C", out var color));
        Assert.Equal(Color.FromArgb(255, 0xC4, 0x51, 0x3C), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C4513C")]
    [InlineData("#C4513")]
    [InlineData("#C4513CZ")]
    [InlineData("#GGHHII")]
    public void TryParse_RefusesAnythingElse(string? hex)
    {
        Assert.False(AccentPalette.TryParse(hex, out _));
    }

    [Fact]
    public void Shades_LightenTowardWhiteAndDarkenTowardBlack()
    {
        Assert.True(AccentPalette.TryParse("#C4513C", out var accent));

        var shades = AccentPalette.Shades(accent);

        Assert.Equal(accent, shades.Base);
        Assert.True(Luma(shades.Light1) > Luma(shades.Base));
        Assert.True(Luma(shades.Light2) > Luma(shades.Light1));
        Assert.True(Luma(shades.Light3) > Luma(shades.Light2));
        Assert.True(Luma(shades.Dark1) < Luma(shades.Base));
        Assert.True(Luma(shades.Dark2) < Luma(shades.Dark1));
        Assert.True(Luma(shades.Dark3) < Luma(shades.Dark2));
    }

    private static int Luma(Color color) => color.R + color.G + color.B;
}
