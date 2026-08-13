#!/usr/bin/env bash
# docs/COMMENTS.md: a comment on a C# declaration is ///, never //.
#
# A // above a declaration is a doc comment that forgot to be one: IntelliSense will not show it,
# the documentation file will not carry it, and CS1591 cannot see that it is there. Only a compiler
# knows what a declaration is, so this approximates: a run of // lines followed by a line that
# opens with an attribute or an access/declaration keyword. Locals inside a body do not match,
# which is why `const` is only counted after a modifier.
set -uo pipefail

root="${1:-.}"
found=0

while IFS= read -r file; do
  awk -v F="$file" '
    /^[ \t]*\/\/[^\/]/ { if (run == 0) start = NR; run = 1; next }
    run == 1 && /^[ \t]*(\[|(public|internal|private|protected|static|sealed|partial|abstract|virtual|override|async|namespace|record|class|interface|enum|struct)[ \t])/ {
      print F ":" start; run = 0; next
    }
    { run = 0 }
  ' "$file"
done < <(git -C "$root" ls-files 'client/src/*.cs' 'client/tests/*.cs' | sed "s|^|$root/|") > /tmp/doc-comment-hits.txt

if [ -s /tmp/doc-comment-hits.txt ]; then
  echo "a comment on a declaration must be /// (docs/COMMENTS.md, Shape):"
  cat /tmp/doc-comment-hits.txt
  found=1
fi

rm -f /tmp/doc-comment-hits.txt
exit $found
