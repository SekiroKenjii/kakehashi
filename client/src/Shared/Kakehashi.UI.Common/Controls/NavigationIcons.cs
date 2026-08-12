using System;
using System.Collections.Generic;
using System.Linq;

namespace Kakehashi.UI.Common.Controls {
  // The server sends "note", not a code point, and the split is deliberate: which code point
  // draws a note is a fact about the font a client ships with, and a server sending code points
  // would be deciding what a Windows font looks like for clients it knows nothing about. It is
  // also why the server accepts any short string - this vocabulary is one client's, not the
  // contract's.
  //
  // An unknown name falls back to whatever the page already declared rather than to a
  // placeholder, so a deployment naming an icon this build has never heard of costs nothing.
  //
  // Glyphs are escapes, never literal characters. They are Private Use Area code points, so a
  // literal shows up as nothing at all in most editors and diffs, which is how a mapping gets
  // silently destroyed by an edit that looks harmless.
  public static class NavigationIcons {
    // One ordered array feeds both the lookup and Names, so the picker cannot come to disagree
    // with what Resolve accepts. The order is the order a picker offers: this build's own
    // screens first.
    private static readonly (string Name, string Glyph)[] _vocabulary = [
      ("home", "\uE80F"),
      ("note", "\uE70B"),
      ("activity", "\uF463"),
      ("people", "\uE716"),
      ("account", "\uE77B"),
      ("permissions", "\uE192"),
      ("navigation", "\uE700"),
      ("settings", "\uE713"),

      // Named for the subject, not for what the glyph draws: the name is what a deployment
      // stores and what another client, shipping a different icon font, has to make sense of.
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

    // For a caller with nothing better to fall back to. Resolve prefers the caller's own
    // fallback, which for the pane is the glyph the page was compiled with; the screen that
    // manages the arrangement never loads the pages, so it has none. Same code point the
    // activity feed draws for a kind it cannot name, because it means the same thing.
    public const string Unknown = "\uE946";

    public static IReadOnlyList<string> Names { get; } =
        _vocabulary.Select(entry => entry.Name).ToArray();

    public static string Resolve(string name, string fallback) {
      if (name.Length == 0) {
        return fallback;
      }
      if (_glyphs.TryGetValue(name, out var glyph)) {
        return glyph;
      }

      // Then the whole font: the short vocabulary is what a deployment is meant to reach for,
      // but somebody who went through the full catalogue picked a real icon and should get it.
      string catalogued = SegoeFluentIcons.Glyph(name);
      return catalogued.Length > 0 ? catalogued : fallback;
    }

    public static bool Knows(string name) {
      return _glyphs.ContainsKey(name) || SegoeFluentIcons.Knows(name);
    }
  }
}
