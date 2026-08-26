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

## template/v1.3.0 — 2026-08-26

| | |
| --- | --- |
| Requires CLI | `>=1.3.0 <2.0.0` — **raised** |

**This release needs CLI 1.3.0.** It asks one question older binaries do not know to ask, so a
1.2.0 CLI is refused by name rather than left to produce a project with an unsubstituted token in
it. Take CLI 1.3.0 first; `kakehashi` on 1.2.0 goes on resolving template 1.2.0 until you do.

### Added

- **A project names its own plugin package extension.** The scaffold asks, suggesting
  `<appname>pkg`, so a packed plugin for Order Desk is `weather-0.1.0.orderdeskpkg` rather than the
  generic `.plugin` every project used to share. The answer is recorded in `.kakehashi.json` with
  the rest, and it is spelled once in the code.

- **A plugin's screens can be filed under a heading you choose.** The Plugins screen offers the
  headings the current pane actually has, and writes the choice to this machine's plugin state, so
  a plugin no longer lands wherever its author happened to compile. Renaming a heading no longer
  forks a second one under the old name. The Navigation screen lists plugin screens read-only under
  *Placed by this client*, because they are arranged from the Plugins screen instead — that limit is
  now visible rather than silent.

### Fixed

- **Plugins sits in the footer**, between the profile row and Settings, where an administrative
  screen belongs rather than in the Administration group. The pane's permission gate now covers
  footer items too — without it the move would have shown the screen to every signed-in account.

- **Icons that resolve.** The Navigation screen is a map-directions sign on both the pane and the
  Navigation screen, from one line; the Plugins icon is OEM everywhere, with no puzzle piece and no
  ⓘ placeholder. Every glyph in the client is a `\uXXXX` escape, and two new CI gates keep it that
  way — one refusing literal private-use characters, one refusing a server-declared icon name
  nothing resolves.

- **Dead space a Grid or a StackPanel was charging for things that render nothing.** A closed
  `InfoBar` is not collapsed — it keeps its slot at zero height — and a `Grid` charges `RowSpacing`
  between every adjacent row pair whether the row measures anything or not. Between them that cost
  37px above the Plugins feed and 12px on Activity, Navigation, Role permissions, Account, Notes and
  the sign-in form. Each fix is measured in the running app, in both directions.

- **Fourteen controls that announced nothing** now have accessible names — including all four
  buttons on the sign-in window, the account flyout's theme toggles, and four lists that carried an
  AutomationId and no name. A control whose content is a panel derives no name from the text inside
  it, and neither a tooltip nor an AutomationId is a label.

## template/v1.2.0 — 2026-08-24

| | |
| --- | --- |
| Requires CLI | `>=1.0.0 <2.0.0` — unchanged |

### Added

- **Plugins.** A module built somewhere else, packaged as a `.plugin` file and installed into a
  running project, becomes a first-class part of it — its services registered in the same
  container, its screens in the same navigation pane, its row on the same screen as the compiled-in
  modules. A new **Plugins** screen manages all of it: what this composition is made of, install
  from a file, browse the catalog the deployment serves, and a Develop tab that scaffolds a plugin
  project which builds without being edited first.

  Installing takes effect at the next launch and removing happens at the start of one. That is the
  shape of the feature rather than a defect in it: `RegisterServices` takes an `IServiceCollection`,
  and that collection stops accepting registrations before any window exists. `docs/PLUGINS.md` is
  the whole of it — what a plugin can and cannot do, what Verified means, and what the loader
  refuses.

- **A plugin's compiled XAML resolves**, which is the part the Windows App SDK does not offer.
  Full XAML, `x:Bind`, code-behind, the host's own styles and a plugin's own declared types all
  work, through a resource fallback and a metadata-provider bridge this template owns — no
  third-party package, no native hooking (ADR 0024). A plugin's own `.resw` strings are the one
  gap, and it is stated rather than discovered.

- **A server-side catalog.** A new `plugins` Go module publishes, lists and serves plugin
  artifacts, streamed over the contract in chunks rather than from a static route this server has
  never had (ADR 0026). Installing from it reaches the activity feed as its own kind, and a
  sideload reaches it as a louder one — without widening `activityapi.CanReport`, because the
  event travels on the bus from a module that checked its own catalog first.

- **`__APP_NAME__.PluginTool`**, a `PackAsTool` CLI a plugin author's build server can install:
  `scaffold`, `validate` and `pack`, over the same library the application runs, so an author and a
  user's installation cannot be told different things. CI scaffolds a plugin against the host it
  just built and compiles it, both with and without a sample page.

- **Authenticode verification** in `__APP_NAME__.Interoperability`: the trust provider's verdict
  and the signer's subject and public key, through CsWin32. Verified means signed with the same key
  as the running executable — the subject and a digest of the key both have to match, because a
  subject alone is a string any authority the machine trusts can issue.

### Fixed

- Choosing the project's accent changed nothing. `AccentService` wrote the accent family into
  application resources after the shell had already drawn, and WinUI binds its accent brushes the
  first time a window draws — so all seven writes landed where nothing would read them again. The
  Windows branch was the same mistake inverted, removing keys that had never been there. The write
  now happens in its own orchestrator ahead of the splash, and Windows becomes a source like any
  other rather than a fallback.

- The activity feed's "open the account screen" action never worked. It navigated to `AccountPage`
  while the navigation service registers the key it derives by dropping the suffix, which is
  `Account`.

### Changed

- The startup order lives in one file. Adding an orchestrator meant opening every other one to find
  out which numbers were taken, and the only list was prose in ADR 0010 that went stale the moment a
  step was added. Each constant now carries the invariant that pins it, and the ADR names its
  anchors rather than restating the values.

## template/v1.1.1 — 2026-08-19

| | |
| --- | --- |
| Requires CLI | `>=1.0.0 <2.0.0` — unchanged |

### Fixed

- The Account page was locked for every account that ever signed in, administrators included, and
  no administrator action could unlock it. The client withheld any module missing its
  `<module>.access` grant, and the account module's routes are deliberately not gated on
  `account.access` — signing in cannot require a permission you only have after signing in — so the
  key it waited for is one the server never mints and no role can hold. A required module is no
  longer the administrator's to withhold.

The Home page card was the visible symptom: `LOCKED`, with the click swallowed. The page itself
stayed reachable from the navigation pane's account row and the account flyout, which is why the
pane and the card disagreed.

## template/v1.1.0 — 2026-08-18

| | |
| --- | --- |
| Requires CLI | `>=1.0.0 <2.0.0` — unchanged |

### Added

- The Home page's static three-gates card is a live **System card**: server version, uptime, one
  row per store answering for itself, and the clock skew. Backed by a new `HealthService.System`
  RPC — additive, so older clients keep working — whose response is bounded to what a public route
  may say: names from the wiring, `ok` and a latency, never an error string. The health module
  gains `store/`, holding no tables: only the probes, because touching a database is store/'s alone.
- Settings offers the **accent choice**: Windows' system accent, or the accent the project was
  scaffolded with — named in the UI by the app's own title. The wizard's `--accent` answer finally
  lands somewhere the app reads (`Branding:Accent` in `appsettings.json`); a project whose recorded
  accent does not parse hides the card rather than offering a switch that cannot switch.

### Fixed

- `docs/getting-started.md` describes the System card in the gates card's place; the gates keep
  their own page in `docs/gates.md`.

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
