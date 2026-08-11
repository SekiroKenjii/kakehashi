using Kakehashi.UI.Common.Controls;
using Xunit;

namespace Kakehashi.App.Tests.UI {
  /// <summary>
  /// Unit tests for <see cref="NavigationIcons"/>: the vocabulary an icon picker offers, and the
  /// fallback that keeps an unknown name harmless.
  /// </summary>
  /// <remarks>
  /// The vocabulary and the lookup are built from one array so a picker cannot offer a name that
  /// <see cref="NavigationIcons.Resolve"/> would then refuse. Touching <c>Names</c> at all is what
  /// proves that array has no duplicate name: two entries with the same name would throw while the
  /// dictionary is being built, and a static initialiser that throws surfaces as an unrelated-looking
  /// failure the first time any screen draws a pane.
  /// </remarks>
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

    /// <summary>
    /// A name this build has never heard of costs nothing: the row keeps the glyph it always drew.
    /// </summary>
    [Fact]
    public void AnUnknownNameKeepsTheGlyphTheCallerAlreadyHad() {
      Assert.Equal(_fallback, NavigationIcons.Resolve("a-name-from-a-later-build", _fallback));
      Assert.False(NavigationIcons.Knows("a-name-from-a-later-build"));
    }

    [Fact]
    public void NoNameAtAllMeansNoOpinion() {
      Assert.Equal(_fallback, NavigationIcons.Resolve(string.Empty, _fallback));
    }

    /// <summary>Names are matched exactly — the server's vocabulary is lower case by convention.</summary>
    [Fact]
    public void MatchingIsOrdinalRatherThanForgiving() {
      Assert.False(NavigationIcons.Knows("Home"));
      Assert.True(NavigationIcons.Knows("home"));
    }
  }
}
