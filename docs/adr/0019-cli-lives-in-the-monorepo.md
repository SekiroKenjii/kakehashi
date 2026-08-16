# 0019. The CLI lives in this repository, versioned by tag prefix

Date: 2026-08-15
Status: accepted

## Context
`kakehashi new` and `kakehashi add module` need a binary, and it can live in its own repository or
in this one. A separate repository gives the CLI its own release cadence and a clean `go install`
path. `docs/pivot/01-PHASE-0-INVENTORY.md` D4 recommends `tools/cli/` here, with versions separated
by tag prefix.

## Decision
`tools/cli/`, in this repository. The generator's source of truth is the `notes` module and the
marker regions in the composition roots, both of which live here; splitting the repositories puts
a version boundary between a template change and the generator that has to match it, and the first
symptom is generated code that fails a gate nobody ran. One repository means one CI run checks the
template, the generated module and all three gates together.

Versions are separated by tag prefix — `template/vX.Y.Z` and `cli/vX.Y.Z` — so the two release on
their own cadence without their own repositories. Phase 5 owns the tagging scheme and the
compatibility range the CLI declares against a template version.

## Consequences
`go install github.com/SekiroKenjii/kakehashi/tools/cli/cmd/kakehashi@latest` resolves against tags
in a repository that also holds a WinUI solution, so the CLI's own module must depend on nothing in
`server/` or `client/` — it is a third Go module beside `server/` and `tools/inventory/`, and
`server`'s `go build ./...` never sees it. CI grows a job per module rather than a job per
repository. If the CLI ever needs a release cadence this repository cannot serve, extracting it is
a mechanical move of one directory plus its tags, which is the reason to start here rather than the
reason not to.
