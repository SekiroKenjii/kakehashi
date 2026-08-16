# 0022 — The CLI's tags carry its module path

Supersedes the tagging half of [0019](0019-cli-lives-in-the-monorepo.md).

## Context

0019 put the CLI in this repository and separated the two release lines by tag prefix,
`template/vX.Y.Z` and `cli/vX.Y.Z`, and said `go install ...@latest` would resolve against those
tags. It does not. `tools/cli` is a Go module inside a repository, and the go command reads a nested
module's versions only from tags prefixed with that module's own directory. `cli/v1.0.0` is not such
a tag, so the module had no versions at all:

```text
$ go list -m -versions github.com/SekiroKenjii/kakehashi/tools/cli
github.com/SekiroKenjii/kakehashi/tools/cli
$ go install github.com/SekiroKenjii/kakehashi/tools/cli/cmd/kakehashi@cli/v1.0.0
go: invalid version: version "cli/v1.0.0" invalid: disallowed version string
```

`@latest` did not fail. It silently served a pseudo-version of the default branch, which is
`development` — the README's first command installing unreleased work, quietly.

## Decision

The CLI is tagged `tools/cli/vX.Y.Z`. The version query keeps no prefix: the tag carries the
directory and `go install ...@vX.Y.Z` takes what is left. The template keeps `template/vX.Y.Z`,
which resolves nothing and needs to satisfy no tool.

## Consequences

One tag serves both the release workflow and the go command, so there is no second tag line to
forget. `@latest` becomes the check that the scheme is right: a `v0.0.0-` pseudo-version means the
tag was not recognised, which is the failure that hid for a whole release.

`cli/v1.0.0` was published before this and stays as it was — a tag pointing at the same commit as
`tools/cli/v1.0.0`, under a name nothing reads. The version line is unbroken because the version
never moved; only the prefix did.
