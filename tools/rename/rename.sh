#!/usr/bin/env bash
# Turns this template into a project: drops the template's own scaffolding, substitutes every
# placeholder, renames every path that holds one, and refuses to finish while a placeholder or an
# identity string survives.
#
# rename.ps1 is the same algorithm for Windows, where the client build lives. Both are the
# reference the Phase 2 CLI ports to Go: no shell-specific cleverness, one pass per step.
set -euo pipefail

root=$(git rev-parse --show-toplevel)
cd "$root"

usage() {
    cat >&2 <<'USAGE'
tools/rename/rename.sh --app-name <PascalCase> --go-module <path> [options]

  --app-name        PascalCase, ^[A-Z][A-Za-z0-9]{1,39}$        required
  --go-module       Go module path                              required
  --app-title       window and package display name             default: --app-name
  --proto-package   proto root, ^[a-z][a-z0-9_]*$               default: lowercased --app-name
  --root-namespace  C# root namespace                           default: --app-name
  --accent          six-digit hex colour                        default: #E34234
  --author          LICENSE and package author                  default: git config user.name
  --year            LICENSE copyright year                      default: this year
USAGE
    exit 2
}

die() { echo "rename: $*" >&2; exit 1; }

app_name=""; go_module=""; app_title=""; proto_package=""; root_namespace=""
accent="#E34234"; author=""; year=""

while [ $# -gt 0 ]; do
    case "$1" in
        --app-name) app_name="${2:-}"; shift 2 ;;
        --go-module) go_module="${2:-}"; shift 2 ;;
        --app-title) app_title="${2:-}"; shift 2 ;;
        --proto-package) proto_package="${2:-}"; shift 2 ;;
        --root-namespace) root_namespace="${2:-}"; shift 2 ;;
        --accent) accent="${2:-}"; shift 2 ;;
        --author) author="${2:-}"; shift 2 ;;
        --year) year="${2:-}"; shift 2 ;;
        -h|--help) usage ;;
        *) echo "rename: unknown argument $1" >&2; usage ;;
    esac
done

# ── 1. validate and derive ─────────────────────────────────────────────────────────────────────
# The regexes are docs/pivot/02-PHASE-1-TEMPLATIZATION.md §1.
[ -n "$app_name" ] || die "--app-name is required"
[ -n "$go_module" ] || die "--go-module is required"

echo "$app_name" | grep -qE '^[A-Z][A-Za-z0-9]{1,39}$' ||
    die "--app-name must match ^[A-Z][A-Za-z0-9]{1,39}\$, got '$app_name'"
echo "$go_module" | grep -qE '^[a-zA-Z0-9][a-zA-Z0-9._~/-]*[a-zA-Z0-9]$' ||
    die "--go-module is not a valid module path: '$go_module'"

app_name_lower=$(echo "$app_name" | tr '[:upper:]' '[:lower:]')
app_name_upper=$(echo "$app_name" | tr '[:lower:]' '[:upper:]')
: "${app_title:=$app_name}"
: "${proto_package:=$app_name_lower}"
: "${root_namespace:=$app_name}"
: "${author:=$(git config user.name 2>/dev/null || echo "$app_name")}"
: "${year:=$(date -u +%Y)}"

echo "$proto_package" | grep -qE '^[a-z][a-z0-9_]*$' ||
    die "--proto-package must match ^[a-z][a-z0-9_]*\$, got '$proto_package'"
echo "$accent" | grep -qE '^#[0-9A-Fa-f]{6}$' ||
    die "--accent must be a six-digit hex colour, got '$accent'"
echo "$root_namespace" | grep -qE '^[A-Z][A-Za-z0-9.]*$' ||
    die "--root-namespace is not a valid C# namespace: '$root_namespace'"
echo "$year" | grep -qE '^[0-9]{4}$' || die "--year must be four digits, got '$year'"

# Longest placeholder first: __APP_NAME_LOWER__ starts with __APP_NAME_, so substituting the short
# one first would leave "OrderDeskLOWER__" behind.
placeholder_names=(
    __APP_NAME_LOWER__ __APP_NAME_UPPER__ __APP_NAME__ __APP_TITLE__
    __ROOT_NAMESPACE__ __PROTO_PACKAGE__ __GO_MODULE__ __ACCENT__ __AUTHOR__ __YEAR__
)
placeholder_values=(
    "$app_name_lower" "$app_name_upper" "$app_name" "$app_title"
    "$root_namespace" "$proto_package" "$go_module" "$accent" "$author" "$year"
)

# sed reads | as the delimiter, & as the whole match and \ as an escape. A value carrying one of
# them would half-apply, which is worse than refusing.
for value in "${placeholder_values[@]}"; do
    case "$value" in
        *'|'* | *'&'* | *'\'*) die "'|', '&' and '\\' are reserved by the substitution: '$value'" ;;
    esac
done

echo "rename: $app_name <$go_module>"

# ── 2. drop what belongs to the template ───────────────────────────────────────────────────────
# These document the boilerplate and name it on every page. templates/units survives: the
# scaffolded project reads it to remove the example module later.
for path in \
    docs/BOILERPLATE.md \
    docs/pivot \
    docs/brand \
    docs/adr/0016-one-example-module-in-the-template.md \
    docs/adr/0017-oidc-provider-is-core.md \
    docs/adr/0018-database-driven-navigation-stays.md \
    docs/adr/0019-cli-lives-in-the-monorepo.md \
    docs/adr/0020-no-second-example-module.md \
    tools/inventory \
    .github/workflows/scaffold-smoke.yml
do
    rm -rf "$path"
done

# Deletes any line mentioning one of the patterns. \|…| rather than /…/ because the patterns are
# paths, and a slash inside a /…/ address ends the address.
drop_lines() {
    local file="$1"; shift
    local expr=""
    for pattern in "$@"; do expr="$expr;\\|$pattern|d"; done
    sed "${expr#;}" "$file" > "$file.tmp" && cat "$file.tmp" > "$file" && rm -f "$file.tmp"
}
drop_lines docs/adr/README.md \
    0016-one-example-module 0017-oidc-provider 0018-database-driven-navigation \
    0019-cli-lives-in-the-monorepo 0020-no-second-example-module
drop_lines README.md docs/brand

# The scaffold README is the project's. The template's own is about the template.
[ -f templates/README.scaffold.md ] && mv -f templates/README.scaffold.md README.md

# ── 3. content ─────────────────────────────────────────────────────────────────────────────────
substitution=""
for i in "${!placeholder_names[@]}"; do
    substitution="$substitution;s|${placeholder_names[$i]}|${placeholder_values[$i]}|g"
done
substitution="${substitution#;}"

substituted=0
while IFS= read -r -d '' file; do
    [ -f "$file" ] || continue
    # These scripts hold the placeholder table itself, and this one is still being read from disk:
    # rewriting it mid-run makes the shell resume at a byte offset into different text.
    case "$file" in tools/rename/*) continue ;; esac
    # -I reports no match in a binary file, which is how an icon is skipped.
    grep -Iq '__[A-Z][A-Z0-9_]*__' "$file" 2>/dev/null || continue
    sed "$substitution" "$file" > "$file.rename.tmp"
    cat "$file.rename.tmp" > "$file"
    rm -f "$file.rename.tmp"
    substituted=$((substituted + 1))
done < <(git ls-files -z)
echo "rename: substituted $substituted files"

# ── 4. paths ───────────────────────────────────────────────────────────────────────────────────
# Deepest first, so renaming a directory never invalidates a path still queued beneath it.
renamed=0
while IFS= read -r path; do
    new="$path"
    for i in "${!placeholder_names[@]}"; do
        new="${new//${placeholder_names[$i]}/${placeholder_values[$i]}}"
    done
    [ "$new" = "$path" ] && continue
    mv "$path" "$new"
    renamed=$((renamed + 1))
done < <(find . -path ./.git -prune -o -name '*__*__*' -print |
    awk -F/ '{print NF"\t"$0}' | sort -rn | cut -f2-)
echo "rename: renamed $renamed paths"

# ── 5. clean ───────────────────────────────────────────────────────────────────────────────────
# XamlCompiler caches the old namespaces under obj/, and a stale cache fails the next build with an
# error naming a type nobody wrote.
find . -path ./.git -prune -o -type d \( -name obj -o -name bin \) -print0 |
    xargs -0 --no-run-if-empty rm -rf
rm -rf .buf-cache
# Unlinking the running script is safe: the shell keeps its descriptor until it exits.
rm -rf tools/rename

# ── 6. self-check ──────────────────────────────────────────────────────────────────────────────
# grep rather than git grep: the paths renamed above are untracked until the next commit, and
# git grep would not look at them.
#
# "kakehashi:" and ".kakehashi.json" are exempt, and are the only two things that are. They are the
# generator's namespace, not the application's: the CLI reads them in the scaffolded project to add
# and remove modules, and renaming them would break the tool rather than finish the rename.
leftovers=$(grep -rInE '__[A-Z][A-Z0-9_]*__|Kakehashi|kakehashi|KAKEHASHI|SekiroKenjii|架け橋' \
    . --exclude-dir=.git 2>/dev/null |
    grep -vE 'kakehashi:[a-z0-9-]+:(begin|end)|\.kakehashi\.json' || true)
if [ -n "$leftovers" ]; then
    echo "rename: the tree still names the template:" >&2
    echo "$leftovers" >&2
    exit 1
fi
echo "rename: self-check clean"

# ── 7. next ────────────────────────────────────────────────────────────────────────────────────
cat <<NEXT

  $app_name is ready.

    docker compose up -d
    curl http://localhost:8080/healthz
    dotnet build client/$app_name.slnx -p:Platform=x64
    dotnet run --project "client/src/App/$app_name.App/$app_name.App.csproj" -p:Platform=x64

  Commit before you start:  git add -A && git commit -m "chore: scaffold from the template"
NEXT
