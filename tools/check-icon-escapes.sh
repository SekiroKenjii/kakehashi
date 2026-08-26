#!/usr/bin/env bash
# Fails when a C# file carries a Private Use Area character instead of a \uXXXX escape.
#
# The glyphs this client draws are PUA code points, and a literal one shows up as nothing in most
# editors, diffs and terminals — which is how a mapping gets silently destroyed by an edit that
# looks harmless (docs/adr/0013-client-owned-icon-vocabulary.md). XAML is exempt: markup spells a
# glyph as the &#xE712; entity, which is already visible text.
#
# Matched by the UTF-8 lead byte 0xEE, which covers U+E000-U+EFFF — every glyph this font ships in.
# The rest of the PUA leads with 0xEF, which also leads the byte-order mark, so it is left alone
# rather than made to produce false refusals.
#
# Usage: tools/check-icon-escapes.sh [repo-root]
set -euo pipefail

root="${1:-.}"
cd "$root"

files=$(git ls-files 'client/src/**/*.cs' 'client/tests/**/*.cs' 'client/tools/**/*.cs')
hits=$(LC_ALL=C grep -n $'\xee' -- $files 2>/dev/null || true)

if [ -n "$hits" ]; then
  echo "a glyph is written as a literal character, which is invisible in editors and diffs; write"
  echo "it as a \\uXXXX escape (docs/adr/0013-client-owned-icon-vocabulary.md):"
  echo "$hits" | cut -d: -f1,2
  exit 1
fi
