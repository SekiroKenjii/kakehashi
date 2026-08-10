using System;
using System.Collections.Generic;

namespace Kakehashi.App.UI {
  /// <summary>
  /// Turns the semantic icon names a deployment uses into the glyphs this build can draw.
  /// </summary>
  /// <remarks>
  /// The server sends "note", not <c></c>, and the split is deliberate. Which code point draws a
  /// note is a fact about the font a client ships with — Segoe Fluent Icons here, something else on a
  /// platform that does not have it — and a server sending code points would be deciding what a
  /// Windows font looks like on behalf of clients it knows nothing about.
  /// <para>
  /// An unknown name falls back to whatever the page already declared rather than to a placeholder.
  /// That way a deployment naming an icon this build has never heard of costs nothing: the row draws
  /// the glyph it always did.
  /// </para>
  /// </remarks>
  internal static class NavigationGlyphs {
    private static readonly Dictionary<string, string> _glyphs = new(StringComparer.Ordinal) {
      ["note"] = "",
      ["activity"] = "",
      ["people"] = "",
      ["permissions"] = "",
      ["navigation"] = "",
      ["home"] = "",
      ["settings"] = "",
      ["account"] = "",
    };

    /// <summary>The glyph for a name, or <paramref name="fallback"/> when there is nothing better.</summary>
    public static string Resolve(string name, string fallback) {
      if (name.Length == 0) {
        return fallback;
      }
      return _glyphs.TryGetValue(name, out var glyph) ? glyph : fallback;
    }
  }
}
