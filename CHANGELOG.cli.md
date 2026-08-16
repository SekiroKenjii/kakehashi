# Changelog — CLI

The CLI's own version line, tagged `tools/cli/vX.Y.Z`. The template has its own:
[CHANGELOG.template.md](CHANGELOG.template.md).

Ordinary semantic versioning for a tool. The interesting number is the range of templates a release
works with — stated by the binary and checked against each template's own `requiresCli`, in both
directions.

## tools/cli/v1.0.0 — 2026-08-16

| | |
| --- | --- |
| Templates supported | `>=1.0.0 <2.0.0` |

The first release, matching `template/v1.0.0`. The two lines start together and are free to diverge
from here; the range above is what holds them together, and it is checked from both sides.

Published first as `cli/v1.0.0`, which `go install` cannot read: `tools/cli` is a Go module, and Go
resolves a module's versions only from tags carrying its own directory. Same binary, same version,
under the tag the tool chain can see — ADR 0022.

### Added

- `kakehashi new` with no arguments opens a wizard: seven questions, a default on every one but the
  app name, and a summary before anything is written.
- The pipeline reports its stages — fetch, verify, apply, check, git — and finishes with a
  copy-pasteable next-steps block.
- The compatibility matrix is checked in both directions. The CLI declares the template range it
  understands, the template declares the CLI range it needs, and a refusal names the side that has
  to move. `add` and `remove` get the same two checks from `.kakehashi.json`, which now records the
  template's `requiresCli`.
- A generated module contributes a getting-started row to the Home page checklist, derived from the
  example module like the rest of the generator's output.

### Changed

- The identity self-check distinguishes the CLI named as a command from the template named as a
  product, so a scaffolded project may tell its reader what to run. The exemption is by position,
  not by line.
- `new`, `add module`, `add page`, `remove module`, `doctor` and `version`, with `--dry-run` on the
  three that write.
