# 0013. The server sends icon names; each client owns its glyph mapping

Date: 2026-08-12
Status: accepted

## Context
Deployments pick the icons for server-driven navigation entries, so some icon representation had
to cross the wire. Sending font code points was rejected: which code point draws a note is a fact
about the font a client ships with — Segoe Fluent Icons on this Windows build, something else on a
platform that does not have it — and a server sending code points would be deciding what a Windows
font looks like on behalf of clients it knows nothing about.

## Decision
The server stores and forwards short semantic names ("note", "dashboard") and accepts any short
string; the vocabulary is one client's, not the contract's. In the WinUI client,
`NavigationIcons.Resolve(name, fallback)` first consults a curated vocabulary of subject-named
icons, then the full generated `SegoeFluentIcons` catalogue (Microsoft's official name table for
the font), and only then returns the caller's fallback — for navigation rows, the glyph the page
was compiled with.

## Consequences
- A deployment naming an icon this build has never heard of costs nothing: the row draws the
  compiled-in glyph, not a placeholder. Callers with no compiled-in glyph (the arrangement screen
  never loads the pages) use `NavigationIcons.Unknown` (`\uE946`), the same mark the activity feed
  draws for a kind it cannot name.
- A client shipping a different icon font writes its own mapping; the names describe what a screen
  is about rather than what a glyph draws, so they still mean something there. Catalogue names
  cross the wire and degrade exactly the same way as vocabulary names.
- One ordered array feeds both `Resolve` and `Names`, so the picker cannot offer a name the
  lookup would reject.
- Glyphs must stay written as `\u` escapes: they are Private Use Area code points, and a literal
  character shows up as nothing in most editors and diffs, which is how a mapping gets silently
  destroyed by an edit that looks harmless.
- `SegoeFluentIcons.cs` is generated from the official table; regenerate it rather than hand-edit.
