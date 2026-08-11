# Brand

The name is the brief. **架け橋 (kakehashi)** is a bridge built across something — the kind you put up
to join two sides that were not joined before. This repository is a WinUI 3 client on one side, a Go
server on the other, and a contract spanning them. The identity should say that and nothing else.

## The mark

Three strokes, and each one is a thing:

| Stroke | Is | Colour |
| --- | --- | --- |
| The long horizontal | the span — the deck you actually cross | white |
| The arch beneath it | what holds the span up | 朱 vermilion |
| The short bar below | the water it crosses | grey |

It is a redraw rather than a replacement. The icon before it was three left-aligned bars — long,
short, medium — meant as a nod to layering. The count survived; what changed is that the three
strokes now mean something you can name.

**The mark contains no character.** The arch is a filled path tapering to nothing at both ends, the
way a brush leaves paper — that is the whole of the Japanese in it. A mark that needs a CJK font to
render is a mark that is sometimes not there, and the app's own icons have to draw at 16px in a
taskbar where no character survives anyway.

[`kakehashi-mark.svg`](kakehashi-mark.svg) — the tile, for icons and favicons.

## The lockup

**架け橋 Kakehashi** — the name in both scripts, one line, kanji first. The tile sits to its left.

The kanji here is *not* decorative: it is half the wordmark, and a machine with no CJK font shows
boxes where it should be. That is a deliberate trade and worth stating plainly, because an earlier
version of this document argued the opposite. The reasoning changed with the design:

- Every platform this is read on — Windows, macOS, GitHub's own renderers, any current Linux — ships
  a CJK fallback. The residual risk is a stripped container image, which is not where a banner is read.
- The lockup is used where a *reader* is, not where a *system* is. The mark, which is used where
  systems are (taskbar, favicon, MSIX tile), still contains no character. The rule holds where it costs
  something.

Above the lockup sits a small pill badge — a status dot and the owner's handle — and below it the
line **"The High-Performance bridge you build across."**

[`kakehashi-lockup.svg`](kakehashi-lockup.svg) — 1200×630, the social-preview size.

## Palette

| Token | Hex | For |
| --- | --- | --- |
| Ink | `#08090C` | the ground everything sits on |
| Plate | `#22242A` → `#131519` | the tile the mark lives in |
| Paper | `#F7F6F4` | the wordmark and the span |
| **朱 Shu** | `#E0503A` | the arch, and the one accent |
| Ember | `#F5A54A` → `#E0503A` | the big numerals, and nothing else |
| Stone | `#8A8D95` | the water, secondary type |
| Rule | `#24272E` | card borders, dividers |
| Grid | `#23252C` | the dot lattice on the ground |

**朱 (shu)** is the vermilion a Japanese bridge is lacquered in — the same red as a torii. It was
chosen over the usual product blue for one reason: it is the colour the name already implies. Used
once per surface. An accent that appears three times is not an accent.

## Typography

No font is shipped or embedded. Type asks for Segoe UI Variable and falls back through `system-ui` —
a README is read on machines nobody chose, and a webfont that fails to load is worse than a system
font that never tried. Japanese falls back through Yu Gothic UI, Noto Sans JP and Hiragino Sans.

Code is set in **Consolas** first. It is what VS Code uses by default on Windows, and its hyphen is
unmistakably a hyphen — an earlier draft used a font whose hyphen filled the whole cell, which turned
`docker compose up -d` into what read as an em dash and an invalid flag.

## The banner

2000×1080, the first thing in the README, full width. [`kakehashi-banner.svg`](kakehashi-banner.svg).

It is a product shot, not an illustration: the editor window in the middle holds the repository's
real `proto/kakehashi/activity/v1/activity.proto`, and the panels around it state numbers that are
true — six modules, sixty archlint packages, ninety days of retention. Nothing on it is a placeholder,
because a banner that overstates is the first thing a reader learns not to trust.

Three constraints, all of them mechanical rather than aesthetic:

- **No `<filter>`.** Filters are the first thing a README renderer strips. Every glow is layered
  radial gradients.
- **No `<style>` block.** Fonts are presentation attributes on each element; a sanitiser that removes
  the stylesheet would take the typography with it.
- **No external reference of any kind.** One file, nothing to fetch.

Centring is measured rather than eyeballed: render the SVG and check the ink's bounding box against
the canvas centre. The optical centre of a lockup is never its geometric one, and 11px of drift is
visible to everyone and explicable by nobody.

## The application's icons

Everything under `client/src/App/Kakehashi.App/Assets/` — the `.ico`, every tile, the splash — is
generated from the mark by `client/scripts/generate-icons.ps1`. Run it after any change to the mark:

```powershell
pwsh client/scripts/generate-icons.ps1
```

Two things about that script are worth knowing:

- **It draws with GDI+, it does not read the SVG.** The script deliberately has no external tooling
  (no ImageMagick, no Inkscape, no browser), so it mirrors the mark's 256-based geometry stroke for
  stroke in code. The geometry therefore lives in two places, and a change to the mark is a change
  to both. The copy is verified, not trusted: the 256px GDI+ render was pixel-diffed against a
  browser render of the SVG — mean channel difference 1.21/765, with every larger difference sitting
  on a stroke boundary where two rasterisers may disagree about anti-aliasing.
- **Small sizes get a floor, not a redesign.** Faithfully scaled to 16px, each stroke is under a
  pixel of ink and the mark turns to smear. So stroke thicknesses are `max(scaled, floor)` — about
  two pixels — and every `max()` degrades to the SVG's exact geometry at 32px and above. Same mark,
  never a second drawing to keep in sync.
