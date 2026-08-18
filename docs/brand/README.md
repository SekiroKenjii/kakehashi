# Brand

The name is the brief. **架け橋 (kakehashi)** is a bridge built across something — the kind you put
up to join two sides that were not joined before. This repository is a WinUI 3 client on one side, a
Go server on the other, and a contract spanning them.

## The mark

A torii on paper. The gate does the same job the name does: it stands where one side becomes the
other, and you pass through it to cross. Vermilion pillars and beams, an ink kasagi across the top,
a paper sky with clouds, and the sun rising behind an ink mound — the whole scene in a dark rounded
frame.

The mark is designer-authored artwork, not generated geometry. The master is
[`kakehashi-mark.svg`](kakehashi-mark.svg) — the app icon at every size, the favicon, and the tile
the lockup and banner embed.

**The mark contains no character.** That rule survives the redesign: a mark that needs a CJK font
to render is a mark that is sometimes not there, and nothing in the torii asks for one.

One caveat worth knowing: the master carries a paper-grain texture built on an SVG `<filter>`.
Renderers that strip filters lose the grain, nothing else. The copies of the tile embedded in the
lockup and the banner omit the grain entirely, because those two files allow no `<filter>` at all
(see below).

## The lockup

**架け橋 Kakehashi** — the name in both scripts, one line, kanji first, in ink on paper. The tile
sits to its left. Above it, the owner's badge; below it, the line **"The High-Performance bridge
you build across"**; along the bottom, the icon's own horizon — the sun rising behind the ink
ground — widened to the full canvas.

The kanji here is *not* decorative: it is half the wordmark, and a machine with no CJK font shows
boxes where it should be. That is a deliberate trade. Every platform this is read on ships a CJK
fallback, and the lockup is used where a *reader* is, not where a *system* is. The mark, which is
used where systems are (taskbar, favicon, MSIX tile), still contains no character.

[`kakehashi-lockup.svg`](kakehashi-lockup.svg) — 1200×630, the social-preview size.

## Palette

Everything comes off the mark; nothing on any brand surface uses a colour the icon does not.

| Token | Hex | For |
| --- | --- | --- |
| Paper | `#F4EADB` | the ground everything sits on |
| Cloud | `#ECDECA` | the soft shapes on paper |
| Sun | `#E7D1B9` | the rising sun, the light pool behind the lockup |
| Ink | `#17181B` | the mound, dark surfaces, the wordmark's near-black `#1F2125` |
| Frame | `#0B0B0F` | the tile's rounded frame |
| Kasagi | `#1C1E20` | the black beam across the top of the gate |
| **鳥居 Torii** | `#C4513C` | the gate, and the one accent |
| Ember | `#E0A24A` → `#C4513C` | the big numerals on the banner, and nothing else |
| Taupe | `#8B7B66` | secondary type on paper |
| Shadow | `#A34131` / `#8F3A2B` | the reds the torii shades itself with |

**Torii vermilion** replaces the previous 朱 shu (`#E0503A`). It is the same idea one step deeper —
the red a gate is lacquered in, taken from the artwork instead of from a swatch book. Used once per
surface. An accent that appears three times is not an accent.

Dark UI panels on the banner (the editor, the cards, the console) keep their own neutral greys —
they are a product screenshot, not brand surfaces, and recolouring a code editor to match a palette
is the kind of lie the banner's own rules forbid.

## Typography

No font is shipped or embedded. Type asks for Segoe UI Variable and falls back through `system-ui` —
a README is read on machines nobody chose, and a webfont that fails to load is worse than a system
font that never tried. Japanese falls back through Yu Gothic UI, Noto Sans JP and Hiragino Sans.

Code is set in **Consolas** first. It is what VS Code uses by default on Windows, and its hyphen is
unmistakably a hyphen — an earlier draft used a font whose hyphen filled the whole cell, which
turned `docker compose up -d` into what read as an em dash and an invalid flag.

## The banner

2000×1080, the first thing in the README, full width. [`kakehashi-banner.svg`](kakehashi-banner.svg).

It is the pivot said as a picture, in the mark's world: paper sky, clouds, the lockup and the claim
on the left — *Boilerplate + CLI* — and on the right a terminal carrying the tool's real output,
`kakehashi new` through its five ticks and `kakehashi add module orders` through the three gates.
Below the terminal, one card per gate: archlint, ArchitectureTests, buf breaking, each with what it
protects. The pills state numbers that are true — two halves, one contract, three gates. Nothing on
it is a placeholder, because a banner that overstates is the first thing a reader learns not to
trust.

The previous banner was a product shot of the app era — an editor window holding the activity proto,
panels counting modules and retention. It described the artifact; this one describes the capability,
which is what the repository now is.

Three constraints, all of them mechanical rather than aesthetic:

- **No `<filter>`.** Filters are the first thing a README renderer strips. This is why the embedded
  tile has no grain.
- **No `<style>` block.** Fonts are presentation attributes on each element; a sanitiser that
  removes the stylesheet would take the typography with it.
- **No external reference of any kind.** One file, nothing to fetch.

Centring is measured rather than eyeballed: render the SVG and check the ink's bounding box against
the canvas centre. The optical centre of a lockup is never its geometric one, and 11px of drift is
visible to everyone and explicable by nobody.

## The application's icons

Everything under `client/src/App/Kakehashi.App/Assets/` — the `.ico`, every tile and target size,
the splash — is exported from the master artwork and committed as the finished raster set. The
manifest-referenced bare names (`SplashScreen.png`, `StoreLogo.png`, the three logo sizes) are
copies of their `scale-100` variants; `app.ico` carries nine frames from 16 to 256 px.

There is no generator script any more. The previous mark was drawn in code, so a PowerShell script
could mirror it in GDI+ and regenerate every asset; this mark is artwork, with clipping, layered
shading and grain that a hand-mirrored drawing routine could only approximate. The rasters ship in
the repository, and a change to the mark is a re-export, not a re-run.
