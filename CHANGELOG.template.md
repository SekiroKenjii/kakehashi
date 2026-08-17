# Changelog — template

The template's own version line, tagged `template/vX.Y.Z`. The CLI has its own:
[CHANGELOG.cli.md](CHANGELOG.cli.md).

What the numbers mean here is not what they mean for a library:

| | |
| --- | --- |
| **MAJOR** | the structure, the markers or the unit format changed, and an older CLI cannot read this template |
| **MINOR** | something was added — a removable unit, a marker section, a capability a project gains |
| **PATCH** | a defect in the template was fixed |

A project is on the template version it was made with, recorded in `.kakehashi.json`. A release is
not something you have to take: see [docs/faq.md](docs/faq.md).

## template/v1.0.2 — 2026-08-16

### Fixed

- `LICENSE` was one file doing two jobs: this repository's licence and the template's placeholder.
  It is now two. The root one names this repository's author; `templates/LICENSE.scaffold` carries
  `__YEAR__` and `__AUTHOR__` and is moved into place at scaffold time, the same way
  `README.scaffold.md` already is.

A scaffolded project gets exactly what it got before — its own name and year in an MIT licence. What
changes is everything that was reading the root file and finding a placeholder.

## template/v1.0.1 — 2026-08-16

### Fixed

- The shipped documentation named the CLI's tag as `cli/vX.Y.Z`. It is `tools/cli/vX.Y.Z`, because
  that is the only prefix `go install` reads (ADR 0022). `CONTRIBUTING.md`, `docs/cli.md` and
  `docs/faq.md` say so.

Documentation only. A project on 1.0.0 gains nothing structural by taking this, and loses nothing by
not.

## template/v1.0.0 — 2026-08-16

The first release. The repository is a boilerplate rather than an application: every identity is a
placeholder, the example module is one removable unit, and a CLI scaffolds from it.

Starting at 1.0.0 rather than 0.1.0 is a statement about the format, not about maturity. MAJOR here
means the structure, the markers or the unit format changed such that an older CLI cannot read the
template — and those are exactly the things a scaffolded project's future upgrade path depends on
(ADR 0021). Committing to them is the point of the number.

### Added

- A getting-started experience in the generated app: the Home page's Backend card carries the
  endpoint, a Retry and the command to start the stack; the checklist reads real state and offers
  its commands with a copy button; a card lists the three gates and how to run each.
- Modules contribute their own checklist row through `IGettingStartedStep`, so `--bare` and
  `kakehashi remove module` shrink the checklist without anybody editing it.
- A scaffolded project gets its own `README.md` and `CLAUDE.md`, written for its own audience
  rather than inherited from the template repository.
- `docs/getting-started.md`, `docs/first-module.md`, `docs/remove-example.md`, `docs/cli.md`,
  `docs/gates.md` and `docs/faq.md` ship with a scaffolded project.
- A WinUI 3 client and a Go server in one repository, joined by one Protocol Buffers contract, with
  the three gates — `archlint`, the client's architecture tests, and `buf breaking` — on every push.
- Placeholder identity throughout, and rename scripts for anyone starting from the "Use this
  template" button rather than from the CLI.
