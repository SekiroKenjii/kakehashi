using Kakehashi.UI.Common.Controls;
using Xunit;

namespace Kakehashi.App.Tests.UI {
  // The vocabulary and the lookup are built from one array so a picker cannot offer a name Resolve
  // would then refuse. Touching Names at all is what proves that array has no duplicate name: two
  // entries with the same name throw while the dictionary is being built, and a static initialiser
  // that throws surfaces as an unrelated-looking failure the first time any screen draws a pane.
  public sealed class NavigationIconsTests {
    private const string _fallback = "the-page-s-own-glyph";

    [Fact]
    public void EveryOfferedNameResolvesToAGlyphOfItsOwn() {
      Assert.NotEmpty(NavigationIcons.Names);

      foreach (var name in NavigationIcons.Names) {
        Assert.True(NavigationIcons.Knows(name), name);
        // A name the picker offers has to draw something other than "whatever you already had",
        // otherwise choosing it looks like it did nothing.
        Assert.NotEqual(_fallback, NavigationIcons.Resolve(name, _fallback));
      }
    }

    [Fact]
    public void AnUnknownNameKeepsTheGlyphTheCallerAlreadyHad() {
      Assert.Equal(_fallback, NavigationIcons.Resolve("a-name-from-a-later-build", _fallback));
      Assert.False(NavigationIcons.Knows("a-name-from-a-later-build"));
    }

    [Fact]
    public void NoNameAtAllMeansNoOpinion() {
      Assert.Equal(_fallback, NavigationIcons.Resolve(string.Empty, _fallback));
    }

    // The vocabulary here is lower case by convention; the font's own catalogue behind it is
    // PascalCase. This used to probe with "Home", which stopped proving anything once the full
    // Segoe catalogue was chained on — "Home" is a real icon in it, so knowing it is correct.
    // "Activity" is not, which is what makes it a probe rather than a coincidence.
    [Fact]
    public void MatchingIsOrdinalRatherThanForgiving() {
      Assert.False(NavigationIcons.Knows("Activity"));
      Assert.True(NavigationIcons.Knows("activity"));
    }

    // A name from the catalogue is still a name rather than a code point, so it crosses the wire
    // and degrades exactly as the curated ones do.
    [Fact]
    public void TheFontsOwnCatalogueIsReachableToo() {
      Assert.True(NavigationIcons.Knows("GlobalNavButton"));
      Assert.NotEqual(_fallback, NavigationIcons.Resolve("GlobalNavButton", _fallback));

      // And it is exact there as well: the catalogue spells it in PascalCase.
      Assert.False(NavigationIcons.Knows("globalnavbutton"));
    }
  }
}
