using System;
using System.Collections.Generic;
using System.Linq;

namespace Kakehashi.UI.Common.Controls {
  /// <summary>
  /// Turns the semantic icon names a deployment uses into the glyphs this build can draw.
  /// </summary>
  /// <remarks>
  /// The server sends "note", not <c></c>, and the split is deliberate. Which code point draws a
  /// note is a fact about the font a client ships with — Segoe Fluent Icons here, something else on a
  /// platform that does not have it — and a server sending code points would be deciding what a
  /// Windows font looks like on behalf of clients it knows nothing about. That is also why the server
  /// accepts any short string: this vocabulary is one client's, not the contract's.
  /// <para>
  /// An unknown name falls back to whatever the page already declared rather than to a placeholder.
  /// That way a deployment naming an icon this build has never heard of costs nothing: the row draws
  /// the glyph it always did.
  /// </para>
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
      ("home", "\uE80F"),
      ("note", "\uE70B"),
      ("activity", "\uF463"),
      ("people", "\uE716"),
      ("account", "\uE77B"),
      ("permissions", "\uE192"),
      ("navigation", "\uE700"),
      ("settings", "\uE713"),
    ];

    private static readonly Dictionary<string, string> _glyphs =
        _vocabulary.ToDictionary(entry => entry.Name, entry => entry.Glyph, StringComparer.Ordinal);

    /// <summary>
    /// The glyph for a name this build cannot draw, where the caller has nothing better to fall back
    /// to.
    /// </summary>
    /// <remarks>
    /// Resolve prefers the caller's own fallback, which for the pane is the glyph the page was compiled
    /// with. The screen that manages the arrangement has no such glyph — it never loads the pages — so
    /// it needs somewhere to land. The same code point the activity feed draws for a kind it cannot
    /// name, because it means the same thing: this build does not recognise what it was given.
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
      return _glyphs.TryGetValue(name, out var glyph) ? glyph : fallback;
    }

    /// <summary>Whether a name is one this build can draw. For the picker's resolved/unresolved mark.</summary>
    public static bool Knows(string name) {
      return _glyphs.ContainsKey(name);
    }
  }
}
