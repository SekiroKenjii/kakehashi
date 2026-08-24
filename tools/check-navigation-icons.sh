#!/usr/bin/env bash
# Fails when a module declares a navigation icon name this client cannot draw.
#
# A destination's DefaultIcon is a semantic name the server stores and the client resolves
# (docs/adr/0013-client-owned-icon-vocabulary.md). Resolution is ordinal, so a name that is in
# neither the curated vocabulary nor the font's own catalogue silently falls back — and the two
# surfaces then disagree, because the pane has a compiled glyph to fall back to and the Navigation
# screen has only the unknown-icon mark. That is invisible until somebody looks at both.
#
# Usage: tools/check-navigation-icons.sh [repo-root]
set -euo pipefail

root="${1:-.}"
cd "$root"

vocabulary="client/src/Shared/__APP_NAME__.UI.Common/Controls/NavigationIcons.cs"
catalogue="client/src/Shared/__APP_NAME__.UI.Common/Controls/SegoeFluentIcons.cs"

# Both files spell an entry ("name", "\uXXXX"); the name is all this needs.
known=$(sed -n 's/.*("\([A-Za-z0-9]*\)", "\\u[0-9A-Fa-f]\{4\}").*/\1/p' "$vocabulary" "$catalogue")

hits=""
for file in $(git ls-files 'server/internal/modules/**/navigation.go'); do
  for name in $(sed -n 's/.*DefaultIcon:[ \t]*"\([^"]*\)".*/\1/p' "$file"); do
    if ! printf '%s\n' "$known" | grep -qx -- "$name"; then
      hits="${hits}${file}: \"${name}\""$'\n'
    fi
  done
done

if [ -n "$hits" ]; then
  echo "a module declares an icon name this client cannot resolve, so the pane and the Navigation"
  echo "screen will draw different things (docs/adr/0013-client-owned-icon-vocabulary.md):"
  printf '%s' "$hits"
  echo "add the name to NavigationIcons.cs, or use one it already knows."
  exit 1
fi
