# Changelog — CLI

The CLI's own version line, tagged `tools/cli/vX.Y.Z`. The template has its own:
[CHANGELOG.template.md](CHANGELOG.template.md).

Ordinary semantic versioning for a tool. The interesting number is the range of templates a release
works with — stated by the binary and checked against each template's own `requiresCli`, in both
directions.

## tools/cli/v1.3.0 — 2026-08-26

| | |
| --- | --- |
| Templates supported | `>=1.0.0 <2.0.0` — unchanged |

**Template 1.3.0 requires this release.** Nothing here narrows what this binary accepts; the bound
moved on the other side, because the template now carries a placeholder only this CLI answers. On
1.2.0 the refusal is accurate and the resolver keeps serving template 1.2.0, so nothing breaks — but
1.3.0 is the one that reads the current template.

### Added

- **`new` asks what a project calls its plugin packages**, suggesting `<appname>pkg` so pressing
  Enter is the right answer. `--plugin-extension` is the flag for `--no-input` and for scripts. The
  answer becomes `__PLUGIN_EXT__` throughout the generated project, so a packed plugin carries the
  product's own extension rather than a generic one shared with every other project.

### Fixed

- **The scaffold manifest records the plugin extension.** `.kakehashi.json` held every answer except
  the one that brands a project's plugin packages, so a later `upgrade` could not reproduce the
  scaffold it was given.

- **`add module` no longer writes the closed-error-bar gap into every new module.** The generator
  derives its templates from the example module, and the fix to that module had to travel with them.

## tools/cli/v1.2.0 — 2026-08-24

| | |
| --- | --- |
| Templates supported | `>=1.0.0 <2.0.0` — unchanged |

Released alongside template 1.2.0, and the binary is unchanged: the plugin system that release
carries is entirely template-side, and 1.1.0 already scaffolds it. The number moves so the two
lines read the same at a glance.

If you are on 1.1.0 there is nothing here to take.

## tools/cli/v1.1.0 — 2026-08-18

| | |
| --- | --- |
| Templates supported | `>=1.0.0 <2.0.0` — unchanged |

### Changed

- The default `--accent` is torii `#C4513C`, the red the mark's gate is lacquered in, replacing the
  retired shu `#E34234`. Only the default moves: a project that chose its accent keeps it, and with
  template 1.1.0 the value now reaches the app's Settings as a choice rather than only the record.

## tools/cli/v1.0.1 — 2026-08-16

| | |
| --- | --- |
| Templates supported | `>=1.0.0 <2.0.0` |

### Fixed

- The `LICENSE` bundled in every download read `Copyright (c) __YEAR__ __AUTHOR__`. The release
  archives copy the repository's own licence, which was the template's placeholder — so the binary
  shipped under a licence naming nobody. It names its author now.

The binary itself is unchanged from 1.0.0.

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
