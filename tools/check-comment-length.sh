#!/usr/bin/env bash
# Fails when a comment inside a function body runs to three lines or more.
#
# Three lines of prose beside a statement is a paragraph, and a paragraph belongs in docs/ or in
# docs/adr with a one-line pointer left behind (docs/COMMENTS.md rule 4). Only `//` blocks count:
# `///` in C# is an XML doc comment and may be as long as the member deserves, and in Go a comment
# above a top-level declaration is godoc, which revive requires.
#
# Usage: tools/check-comment-length.sh [repo-root]
set -euo pipefail

root="${1:-.}"
cd "$root"

# C#: every `//` block, wherever it sits. A `//` block above a declaration is already refused by
# tools/check-doc-comments.sh, so anything this finds is inline.
cs_hits=$(
  for file in $(git ls-files 'client/src/**/*.cs' 'client/tests/**/*.cs'); do
    awk -v F="$file" '
      /^[ \t]*\/\/([^\/]|$)/ { if (run == 0) start = NR; run++; next }
      { if (run >= 3) print F ":" start " (" run " lines)"; run = 0 }
      END { if (run >= 3) print F ":" start " (" run " lines)" }' "$file"
  done
)

# Go: only inside a function body. Above a declaration the same `//` is godoc.
go_hits=$(
  for file in $(git ls-files 'server/**/*.go' | grep -v '/gen/'); do
    awk -v F="$file" '
      infunc == 0 && /^func / { infunc = 1; depth = 0; opened = 0 }
      infunc == 1 {
        if (/^[ \t]*\/\/([^\/]|$)/) { if (run == 0) start = NR; run++; next }
        if (run >= 3) print F ":" start " (" run " lines)"
        run = 0
        line = $0; opens = gsub(/{/, "", line)
        line = $0; closes = gsub(/}/, "", line)
        if (opens > 0) opened = 1
        depth += opens - closes
        if (opened && depth <= 0) infunc = 0
      }' "$file"
  done
)

hits=$(printf '%s\n%s' "$cs_hits" "$go_hits" | grep -v '^$' || true)
if [ -n "$hits" ]; then
  echo "a comment beside a statement runs to three lines or more; move the argument into docs/ or"
  echo "docs/adr and leave a one-line pointer (docs/COMMENTS.md rule 4):"
  echo "$hits"
  exit 1
fi
