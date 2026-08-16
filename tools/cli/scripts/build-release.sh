#!/usr/bin/env bash
#
# Builds the CLI for every platform it is published for, packages each one, and writes the
# checksums beside them.
#
# goreleaser would do this, and does not: releasing two version lines out of one repository needs
# its tag-prefix support, which is a Pro feature. docs/pivot/06-PHASE-5-RELEASE.md §1.3 allows a
# script, and a script is also the thing somebody can run before pushing a tag.
#
# Usage: scripts/build-release.sh <version> [output-directory]
#
#   scripts/build-release.sh 0.1.0
#   scripts/build-release.sh 0.1.0 /tmp/dist
set -euo pipefail

version="${1:?usage: build-release.sh <version> [output-directory]}"
version="${version#cli/}"
version="${version#v}"

if ! printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "version must be major.minor.patch, got '$version'" >&2
    exit 2
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${2:-$here/dist}"

# The variable the binary reports its own version through. The compatibility matrix is decided on
# what the binary says it is, so a build that forgets this ships claiming to be the development
# default.
version_var="github.com/SekiroKenjii/kakehashi/tools/cli/internal/cli.version"

# Platform, architecture, and the archive format its users expect.
targets=(
    "linux amd64 tar.gz"
    "linux arm64 tar.gz"
    "darwin amd64 tar.gz"
    "darwin arm64 tar.gz"
    "windows amd64 zip"
    "windows arm64 zip"
)

# Title case, because that is what the archive names and the packaging manifests use.
title() {
    case "$1" in
    linux) echo Linux ;;
    darwin) echo Darwin ;;
    windows) echo Windows ;;
    *) echo "$1" ;;
    esac
}

rm -rf "$out"
mkdir -p "$out"

for target in "${targets[@]}"; do
    read -r os arch format <<<"$target"

    binary="kakehashi"
    if [ "$os" = "windows" ]; then
        binary="kakehashi.exe"
    fi

    stage="$out/.stage/${os}_${arch}"
    mkdir -p "$stage"
    cp "$here/../../LICENSE" "$stage/LICENSE"

    echo "building $os/$arch"
    (
        cd "$here"
        CGO_ENABLED=0 GOOS="$os" GOARCH="$arch" go build \
            -trimpath \
            -ldflags "-s -w -X ${version_var}=${version}" \
            -o "$stage/$binary" \
            ./cmd/kakehashi
    )

    name="kakehashi_${version}_$(title "$os")_${arch}"
    if [ "$format" = "zip" ]; then
        (cd "$stage" && zip -q -r "$out/$name.zip" .)
    else
        tar -czf "$out/$name.tar.gz" -C "$stage" .
    fi
done

rm -rf "$out/.stage"

# sha256sum's own format, which is what the template resolver already parses and what winget and
# scoop both read.
(cd "$out" && sha256sum ./*.tar.gz ./*.zip | sed 's| \./| |' > checksums.txt)

echo
echo "$out:"
ls -l "$out"
