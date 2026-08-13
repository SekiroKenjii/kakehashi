using System;
using System.Collections.Generic;
using System.Linq;

namespace Kakehashi.UI.Common.Controls {
  /// <summary>
  /// Turns the semantic icon names a deployment uses into the glyphs this build can draw.
  /// </summary>
  /// <remarks>
  /// The server sends semantic names, never code points; each client owns its glyph mapping, and
  /// an unknown name falls back to the caller's glyph:
  /// docs/adr/0013-client-owned-icon-vocabulary.md
  /// <para>
  /// The glyphs are written as escapes rather than as literal characters. They are Private Use Area
  /// code points, so a literal shows up as nothing at all in most editors and diffs — which is how a
  /// mapping gets silently destroyed by an edit that looks harmless.
  /// </para>
  /// </remarks>
  public static class NavigationIcons {
    /// <summary>
    /// The vocabulary, in the order a picker should offer it.
    /// </summary>
    /// <remarks>
    /// One ordered array feeds both the lookup and <see cref="Names"/>, so the picker cannot come to
    /// disagree with what <see cref="Resolve"/> will accept.
    /// </remarks>
    private static readonly (string Name, string Glyph)[] _vocabulary = [
      // The screens this product ships, first, so the common case is the short walk.
      ("home", "\uE80F"),
      ("note", "\uE70B"),
      ("activity", "\uF463"),
      ("people", "\uE716"),
      ("account", "\uE77B"),
      ("permissions", "\uE192"),
      ("navigation", "\uE700"),
      ("settings", "\uE713"),

      // What somebody else's module is likely to be about. Named for the subject, not the glyph:
      // the name is what a deployment stores and what a client with another font must resolve.
      ("dashboard", "\uF246"),
      ("report", "\uE9D2"),
      ("folder", "\uE8B7"),
      ("document", "\uE8A5"),
      ("calendar", "\uE787"),
      ("mail", "\uE715"),
      ("message", "\uE8BD"),
      ("alerts", "\uEA8F"),
      ("tasks", "\uE9D5"),
      ("search", "\uE721"),
      ("tag", "\uE8EC"),
      ("favourite", "\uE734"),
      ("history", "\uE81C"),
      ("security", "\uE72E"),
      ("database", "\uE964"),
      ("cloud", "\uE753"),
      ("device", "\uE770"),
      ("integration", "\uE839"),
      ("tools", "\uE90F"),
      ("help", "\uE897"),
    ];

    private static readonly Dictionary<string, string> _glyphs =
        _vocabulary.ToDictionary(entry => entry.Name, entry => entry.Glyph, StringComparer.Ordinal);

    /// <summary>
    /// The glyph for a name this build cannot draw, where the caller has nothing better to fall back
    /// to.
    /// </summary>
    /// <remarks>
    /// For callers with no glyph of their own to fall back to — the arrangement screen never loads
    /// the pages, so it has none. The same code point the activity feed draws for a kind it cannot
    /// name: both mean "this build does not recognise what it was given".
    /// </remarks>
    public const string Unknown = "\uE946";

    /// <summary>Every name this build can draw, for an icon picker to offer.</summary>
    public static IReadOnlyList<string> Names { get; } =
        _vocabulary.Select(entry => entry.Name).ToArray();

    /// <summary>The glyph for a name, or <paramref name="fallback"/> when there is nothing better.</summary>
    public static string Resolve(string name, string fallback) {
      if (name.Length == 0) {
        return fallback;
      }
      if (_glyphs.TryGetValue(name, out var glyph)) {
        return glyph;
      }

      // Fall through to the full font catalogue: a name picked from it is still a real icon and is
      // honoured even though deployments are meant to use the short vocabulary above.
      string catalogued = SegoeFluentIcons.Glyph(name);
      return catalogued.Length > 0 ? catalogued : fallback;
    }

    /// <summary>Whether a name is one this build can draw. For the picker's resolved/unresolved mark.</summary>
    public static bool Knows(string name) {
      return _glyphs.ContainsKey(name) || SegoeFluentIcons.Knows(name);
    }
  }
}
