# Boilerplate Map

Every tracked file, in exactly one of three groups. This document is the input to Phase 1
(placeholders and rename), Phase 2 (`kakehashi new`) and Phase 3 (`kakehashi add module`) —
`docs/pivot/01-PHASE-0-INVENTORY.md` is the phase that produced it.

## Legend

| Group | Means |
| --- | --- |
| **CORE** | the frame. It is either shipped into every scaffolded project, or it is the machinery that ships it. |
| **EXAMPLE** | demonstration content. Removable as a named unit, with nothing left behind that fails a gate. |
| **IDENTITY** | the name, the mark, the palette and the prose that make this Kakehashi rather than a template. Dropped or replaced with neutral content when a project is scaffolded. |

- **(M)** — hybrid file: it belongs to its group but holds regions of another, delimited by
  `kakehashi:<section>:begin` / `:end`. The Notes column names the sections.
- A row whose path ends in `/` classifies the directory. **The most specific row wins**, so a file
  row overrides the directory row above it.
- "template repo only" in the Notes column means the file is CORE to this repository and is *not*
  copied into a scaffolded project. It is collected in one place under
  [Not copied into a scaffolded project](#not-copied-into-a-scaffolded-project).

## Checking the map

```sh
cd tools/inventory
go run .             # CSV: path, match, line, suggested_group
go run . -coverage   # fails on a tracked file no row classifies, or a row that covers nothing
```

The scan is the automated half; every row below is the manual half. `-coverage` is what stops the
two drifting: a file added without a row fails it, and so does a row left behind by a deletion.

## Map

### Repository root

| Path | Group | Notes |
| --- | --- | --- |
| `.claude/` | CORE | agent commands and skills; Phase 1 rewrites them for the scaffolded project |
| `.claude/skills/ui-testing/ui-tests.ps1` | CORE | its page list names the example screens — Phase 1 derives it |
| `.editorconfig` | CORE | |
| `.gitattributes` | CORE | |
| `.github/workflows/ci.yml` | CORE | placeholders: `.slnx` and `.csproj` paths |
| `.github/workflows/scaffold-smoke.yml` | CORE | template repo only — renames the tree, then runs the gates |
| `.gitignore` | CORE | |
| `.vscode/` | CORE | placeholders: `KAKEHASHI_*` variable names, the development DSN |
| `CLAUDE.md` | CORE | Phase 1 splits it: this repository's, and the scaffolded project's |
| `CONTRIBUTING.md` | CORE | |
| `LICENSE` | CORE | placeholders: `__AUTHOR__`, year |
| `README.md` | IDENTITY | the template repository's own README; the rename replaces it with `templates/README.scaffold.md` |
| `buf.gen.yaml` | CORE | placeholder: `__GO_MODULE__` in `go_package_prefix` |
| `buf.yaml` | CORE | |
| `docker-compose.yml` | CORE | placeholders: project, database, container and `KAKEHASHI_*` names |
| `templates/README.scaffold.md` | CORE | template repo only — becomes the scaffolded project's README |
| `templates/units/` | CORE | the removable-unit files; these ship, so a project can still remove the example |
| `tools/check-comment-length.sh` | CORE | |
| `tools/check-doc-comments.sh` | CORE | |
| `tools/inventory/` | CORE | template repo only — the scanner and the coverage check |
| `tools/rename/` | CORE | template repo only — the rename scripts, which delete themselves |
| `tools/units/` | CORE | template repo only — applies a removable unit before the rename |

### docs/

| Path | Group | Notes |
| --- | --- | --- |
| `docs/ACTIVITY.md` | EXAMPLE | unit `activity` |
| `docs/ARCHITECTURE.md` | CORE | |
| `docs/BOILERPLATE.md` | CORE | template repo only — this file |
| `docs/COMMENTS.md` | CORE | |
| `docs/CONTRACTS.md` | CORE | |
| `docs/NAVIGATION.md` | CORE | |
| `docs/RBAC.md` | CORE | the mechanism. The seeded roles it describes belong to unit `admin-ui` |
| `docs/adr/` | CORE | |
| `docs/adr/0016-one-example-module-in-the-template.md` | CORE | template repo only — D1 |
| `docs/adr/0017-oidc-provider-is-core.md` | CORE | template repo only — D2 |
| `docs/adr/0018-database-driven-navigation-stays.md` | CORE | template repo only — D3 |
| `docs/adr/0019-cli-lives-in-the-monorepo.md` | CORE | template repo only — D4 |
| `docs/adr/0020-no-second-example-module.md` | CORE | template repo only — D5 |
| `docs/brand/` | IDENTITY | drop from the template; the torii vermilion `#C4513C` becomes the default `__ACCENT__` |
| `docs/pivot/` | CORE | template repo only — the pivot plan |

### proto/

| Path | Group | Notes |
| --- | --- | --- |
| `proto/__PROTO_PACKAGE__/account/v1/` | CORE | placeholders: directory name, `package`, `go_package`, `csharp_namespace` |
| `proto/__PROTO_PACKAGE__/activity/v1/` | EXAMPLE | unit `activity` |
| `proto/__PROTO_PACKAGE__/authz/v1/` | CORE | |
| `proto/__PROTO_PACKAGE__/health/v1/` | CORE | |
| `proto/__PROTO_PACKAGE__/navigation/v1/` | CORE | |
| `proto/__PROTO_PACKAGE__/plugins/v1/` | CORE | |
| `proto/__PROTO_PACKAGE__/notes/v1/` | EXAMPLE | unit `notes` |

### server/

| Path | Group | Notes |
| --- | --- | --- |
| `server/.dockerignore` | CORE | |
| `server/.golangci.yml` | CORE | |
| `server/Dockerfile` | CORE | placeholder: binary name |
| `server/Makefile` | CORE | placeholder: binary name |
| `server/go.mod` | CORE | placeholder: `__GO_MODULE__` |
| `server/go.sum` | CORE | |
| `server/cmd/server/main.go` | CORE (M) | markers: `module-imports`, `module-registrations` |
| `server/cmd/server/main_test.go` | CORE (M) | marker: `module-ids` |
| `server/internal/app/` | CORE | the kernel |
| `server/internal/gen/__PROTO_PACKAGE__/account/v1/` | CORE | generated; the `kakehashi` path segment is `__PROTO_PACKAGE__` |
| `server/internal/gen/__PROTO_PACKAGE__/activity/v1/` | EXAMPLE | unit `activity` |
| `server/internal/gen/__PROTO_PACKAGE__/authz/v1/` | CORE | |
| `server/internal/gen/__PROTO_PACKAGE__/health/v1/` | CORE | |
| `server/internal/gen/__PROTO_PACKAGE__/navigation/v1/` | CORE | |
| `server/internal/gen/__PROTO_PACKAGE__/plugins/v1/` | CORE | generated |
| `server/internal/gen/__PROTO_PACKAGE__/notes/v1/` | EXAMPLE | unit `notes` |
| `server/internal/modules/account/` | CORE | the OpenID Connect provider — D2 |
| `server/internal/modules/activity/` | EXAMPLE | unit `activity` |
| `server/internal/modules/authz/` | CORE | the permission mechanism — D2 |
| `server/internal/modules/health/` | CORE | |
| `server/internal/modules/navigation/` | CORE | four modules implement its `Contributor` contract — D3 |
| `server/internal/modules/plugins/` | CORE | the plugin catalog and its artifacts |
| `server/internal/modules/notes/` | EXAMPLE | unit `notes` |
| `server/internal/platform/` | CORE | |
| `server/tools/archlint/` | CORE | gate 1. Its fixtures name `notes`; Phase 1 makes them synthetic |

### client/ — solution and configuration

| Path | Group | Notes |
| --- | --- | --- |
| `client/.editorconfig` | CORE | |
| `client/Directory.Build.props` | CORE | |
| `client/Directory.Packages.props` | CORE | |
| `client/__APP_NAME__.slnx` | CORE (M) | file renamed by placeholder; markers: `module-projects`, `module-test-projects` |
| `client/README.md` | CORE | |
| `client/Version.props` | CORE | |
| `client/global.json` | CORE | |
| `client/nuget.config` | CORE | |
| `client/scripts/` | CORE | |
| `client/tests/.editorconfig` | CORE | |

### client/docs/

| Path | Group | Notes |
| --- | --- | --- |
| `client/docs/architecture.md` | CORE | |
| `client/docs/csharp-style.md` | CORE | |
| `client/docs/module-attachment-plan.md` | CORE | |
| `client/docs/testing-strategy.md` | CORE | its counts name the example suites |
| `client/docs/mockups/account-page-mockup.html` | CORE | |
| `client/docs/mockups/activity-page-mockup.html` | EXAMPLE | unit `activity` |
| `client/docs/mockups/home-page-mockup.html` | CORE | |
| `client/docs/mockups/navigation-management-mockup.html` | EXAMPLE | unit `admin-ui` |
| `client/docs/mockups/permission-management.html` | EXAMPLE | unit `admin-ui` |
| `client/docs/mockups/profile-flyout-mockup.html` | CORE | |
| `client/docs/mockups/sign-in-ui-mockup.html` | CORE | |
| `client/docs/mockups/splash-screen-ui-mockup.html` | CORE | |
| `client/docs/mockups/users-management.html` | EXAMPLE | unit `admin-ui` |

### client/src/ — host

| Path | Group | Notes |
| --- | --- | --- |
| `client/src/App/__APP_NAME__.App.Infrastructure/` | CORE | directory and project renamed by placeholder |
| `client/src/App/__APP_NAME__.App/` | CORE | directory and project renamed by placeholder |
| `client/src/App/__APP_NAME__.App/Assets/` | IDENTITY | the torii mark at every size; Phase 1 replaces it with neutral art |
| `client/src/App/__APP_NAME__.App/Composition/ModuleCatalog.cs` | CORE (M) | markers: `module-imports`, `module-registrations` |
| `client/src/App/__APP_NAME__.App/__APP_NAME__.App.csproj` | CORE (M) | marker: `module-projects` |
| `client/src/App/__APP_NAME__.App/Package.appxmanifest` | CORE | placeholders: `Identity Name`, `Publisher`, `DisplayName`, `PublisherDisplayName` |
| `client/src/App/__APP_NAME__.App/Services/AccessAdminService.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/Services/NavigationAdminService.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/AdminFormat.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/HomePage.ViewModel.cs` | CORE | `_shippedModuleCount` counts the shipped modules by hand; Phase 1 derives it from `ModuleCatalog` |
| `client/src/App/__APP_NAME__.App/UI/HostNavigation.cs` | CORE | its three entries belong to unit `admin-ui`; marked in Phase 1 |
| `client/src/App/__APP_NAME__.App/UI/NavigationLayoutPage.Nodes.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/NavigationLayoutPage.ViewModel.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/NavigationLayoutPage.xaml` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/NavigationLayoutPage.xaml.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/RolePermissionsPage.ViewModel.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/RolePermissionsPage.xaml` | EXAMPLE | unit `admin-ui`; hardcodes `#C42B1C` on the toggles |
| `client/src/App/__APP_NAME__.App/UI/RolePermissionsPage.xaml.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/UsersPage.ViewModel.cs` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/UsersPage.xaml` | EXAMPLE | unit `admin-ui` |
| `client/src/App/__APP_NAME__.App/UI/UsersPage.xaml.cs` | EXAMPLE | unit `admin-ui` |

### client/src/ — modules and shared

| Path | Group | Notes |
| --- | --- | --- |
| `client/src/Modules/Activity/` | EXAMPLE | unit `activity` |
| `client/src/Modules/Auth/` | CORE | sign-in, session and account — D2 |
| `client/src/Modules/Notes/` | EXAMPLE | unit `notes` |
| `client/src/Shared/` | CORE | every directory and project renamed by placeholder |

### client/tests/

| Path | Group | Notes |
| --- | --- | --- |
| `client/tests/__APP_NAME__.Analyzers.Tests/` | CORE | |
| `client/tests/__APP_NAME__.App.Infrastructure.Tests/` | CORE | |
| `client/tests/__APP_NAME__.App.Tests/` | CORE | `"Notes"` appears as a fake module name; it names no real assembly |
| `client/tests/__APP_NAME__.App.Tests/UI/AccessAdminViewModelTests.cs` | EXAMPLE | unit `admin-ui` |
| `client/tests/__APP_NAME__.App.Tests/UI/NavigationLayoutViewModelTests.cs` | EXAMPLE | unit `admin-ui` |
| `client/tests/__APP_NAME__.ArchitectureTests/` | CORE | gate 2 |
| `client/tests/__APP_NAME__.ArchitectureTests/ActivityLayeringTests.cs` | EXAMPLE | unit `activity` |
| `client/tests/__APP_NAME__.ArchitectureTests/__APP_NAME__.ArchitectureTests.csproj` | CORE (M) | marker: `module-projects` |
| `client/tests/__APP_NAME__.ArchitectureTests/NotesLayeringTests.cs` | EXAMPLE | unit `notes` |
| `client/tests/__APP_NAME__.Mediator.Tests/` | CORE | |
| `client/tests/__APP_NAME__.PluginSdk.Abstractions.Tests/` | CORE | |
| `client/tests/__APP_NAME__.Modules.Activity.Application.Tests/` | EXAMPLE | unit `activity` |
| `client/tests/__APP_NAME__.Modules.Activity.UI.Tests/` | EXAMPLE | unit `activity` |
| `client/tests/__APP_NAME__.Modules.Auth.Application.Tests/` | CORE | |
| `client/tests/__APP_NAME__.Modules.Auth.Domain.Tests/` | CORE | |
| `client/tests/__APP_NAME__.Modules.Auth.IntegrationTests/` | CORE | |
| `client/tests/__APP_NAME__.Modules.Auth.UI.Tests/` | CORE | |
| `client/tests/__APP_NAME__.Modules.Notes.Application.Tests/` | EXAMPLE | unit `notes` |
| `client/tests/__APP_NAME__.Modules.Notes.Domain.Tests/` | EXAMPLE | unit `notes` |
| `client/tests/__APP_NAME__.Modules.Notes.IntegrationTests/` | EXAMPLE | unit `notes` |
| `client/tests/__APP_NAME__.Modules.Notes.UI.Tests/` | EXAMPLE | unit `notes` |
| `client/tools/__APP_NAME__.Analyzers/` | CORE | |
| `client/tools/__APP_NAME__.Analyzers.CodeFixes/` | CORE | |
| `client/tools/__APP_NAME__.PluginTool/` | CORE | |

## Not copied into a scaffolded project

CORE to this repository, absent from what `kakehashi new` writes:

```text
docs/BOILERPLATE.md
docs/pivot/
docs/brand/
docs/adr/0016-…  through  docs/adr/0020-…
templates/README.scaffold.md   (moved to README.md)
tools/inventory/
tools/rename/
tools/units/
.github/workflows/scaffold-smoke.yml
```

`tools/rename/rename.sh` deletes exactly this list, and its self-check fails if anything it left
behind still names the template.

Everything else marked IDENTITY is dropped or neutralised rather than merely skipped.

## Markers

A marker is a comment in the file's own comment token, in the exact form
`kakehashi:<name>:begin` / `kakehashi:<name>:end`. Two kinds, and they nest:

| Kind | Name | What it delimits |
| --- | --- | --- |
| section | `module-imports` | where module imports and `using` directives live |
| section | `module-registrations` | where modules are mounted |
| section | `module-ids` | where module IDs are named as strings |
| section | `module-projects` | where module projects are referenced |
| section | `module-test-projects` | where module test projects are referenced |
| unit | `unit-<id>` | one unit's contribution, inside a section |

A section says *where a generator writes*. A unit marker inside it says *whose lines these are*, and
is what a removal takes back out. Lines inside a section but in no unit marker belong to the frame
and are never removed — that is the whole distinction the two kinds carry.

Invariant: **one unit block per unit per section.** `kakehashi add module` inserts one; removal
deletes it; running the generator twice for the same id is an error rather than a second block.

Where they are, today:

| File | Sections |
| --- | --- |
| `server/cmd/server/main.go` | `module-imports`, `module-registrations` |
| `server/cmd/server/main_test.go` | `module-ids` |
| `client/__APP_NAME__.slnx` | `module-projects`, `module-test-projects` |
| `client/src/App/__APP_NAME__.App/Composition/ModuleCatalog.cs` | `module-imports`, `module-registrations` |
| `client/src/App/__APP_NAME__.App/__APP_NAME__.App.csproj` | `module-projects` |
| `client/tests/__APP_NAME__.ArchitectureTests/__APP_NAME__.ArchitectureTests.csproj` | `module-projects` |

Two facts a marker engine has to know:

- **gofmt is safe.** A comment line is a sort boundary inside a Go import block, so gofmt neither
  moves an import across a marker nor re-sorts a region after an insert. Only indentation is fixed.
- **`modules()` is ordered, not sorted.** The list in `main.go` decides migration order and the
  reverse shutdown order. A generator that inserts alphabetically there changes behaviour; it
  appends instead.

## Removable units

A unit is a closed list of paths plus the marker regions that wire them in. Removing it leaves a
tree that still passes every gate.

### `notes` — defined, verified

`templates/units/notes.json`. The one example the template ships (D5), and what `kakehashi new
--bare` takes back out.

```text
paths    proto/__PROTO_PACKAGE__/notes/v1/
         server/internal/gen/__PROTO_PACKAGE__/notes/v1/
         server/internal/modules/notes/
         client/src/Modules/Notes/
         client/tests/__APP_NAME__.ArchitectureTests/NotesLayeringTests.cs
         client/tests/__APP_NAME__.Modules.Notes.Application.Tests/
         client/tests/__APP_NAME__.Modules.Notes.Domain.Tests/
         client/tests/__APP_NAME__.Modules.Notes.IntegrationTests/
         client/tests/__APP_NAME__.Modules.Notes.UI.Tests/

markers  server/cmd/server/main.go                     module-imports, module-registrations
         server/cmd/server/main_test.go                module-ids
         client/__APP_NAME__.slnx                         module-projects, module-test-projects
         client/src/App/…/Composition/ModuleCatalog.cs  module-imports, module-registrations
         client/src/App/…/__APP_NAME__.App.csproj          module-projects
         client/tests/…/__APP_NAME__.ArchitectureTests.csproj  module-projects
```

Nothing outside those paths references a Notes assembly or the `notes` Go packages. What remains
after removal is prose and test fixtures — see [Residue](#residue).

### `activity` — markers in place, unit file in Phase 1

D1 sends it to `showcase`. The marker regions are already cut for it, so the unit file is a list,
not a refactor. Its paths mirror `notes`, plus `docs/ACTIVITY.md` and
`client/docs/mockups/activity-page-mockup.html`.

### `admin-ui` — drafted, unit file in Phase 1

The RBAC and navigation-layout screens, per D1 and D3. Membership is the EXAMPLE rows above with
`unit admin-ui` in their Notes, and it needs two markers that do not exist yet:
`HostNavigation.Items` and the DI registrations in `Hosting/AppHost.cs`. Whether the server's admin
surface leaves with it is deliberately not decided here — see
[`docs/adr/0016-one-example-module-in-the-template.md`](adr/0016-one-example-module-in-the-template.md).

## Verification

`templates/units/notes.json` applied on a scratch branch cut from this one, then every gate that
runs without Windows. 48 files deleted and 6 marker regions stripped, 4582 lines in all; 638
tracked files became 590.

| Gate | Result |
| --- | --- |
| `buf lint` | pass |
| `buf generate` + diff against the committed tree | clean — no notes output regenerated |
| `gofmt -l` | clean |
| `go build ./...` | pass |
| `go vet ./...` | pass |
| `go test ./...` | pass |
| `go run ./tools/archlint` | pass — 53 packages, no boundary violations (61 with notes) |
| `tools/check-comment-length.sh`, `tools/check-doc-comments.sh` | pass |
| `client` build, `dotnet format`, `__APP_NAME__.ArchitectureTests` | **not run** — needs Windows and the .NET SDK |

The client half of the proof is the marker edits plus deleting whole projects and their `.slnx`
and `.csproj` references; it is checked on Windows before Phase 1 closes.

### Residue

Removing the unit leaves no compile-path reference and no failing gate. It does leave text, and one
piece of behaviour:

| Where | What |
| --- | --- |
| `client/src/App/__APP_NAME__.App/UI/HomePage.ViewModel.cs` | `_shippedModuleCount = 3` no longer matches the shipped modules, so the "register your first module" step misreports |
| `.claude/skills/ui-testing/ui-tests.ps1` | walks a hardcoded page list containing `Notes` |
| `server/internal/modules/health/module.go`, `server/internal/platform/*`, `server/internal/app/*` | comments citing `notes/` as the module to copy |
| `server/tools/archlint/main.go`, `main_test.go` | `notes` as the fixture module name |
| `client/src/Shared/__APP_NAME__.SharedKernel/Error.cs`, `client/src/Shared/__APP_NAME__.UI.Contracts/NavigationItem.cs`, `client/src/Shared/__APP_NAME__.Contracts/__APP_NAME__.Contracts.csproj` | `Notes` in doc-comment examples |
| `client/tests/__APP_NAME__.App.Tests/` | `"Notes"` as a fake module name in fixtures |
| `docs/`, `CLAUDE.md`, `README.md` | prose |

Phase 1 neutralises the first two, because they are wrong rather than merely stale. The rest is
prose the template README and CLAUDE.md rewrite covers.

## Identity strings inside CORE files

A CORE file is kept and its identity substituted; it does not become IDENTITY for holding a name.
What the scanner finds, and what Phase 1 turns each into:

| Literal | Placeholder | Where it is dense |
| --- | --- | --- |
| `Kakehashi` | `__APP_NAME__` / `__ROOT_NAMESPACE__` | every C# namespace, project and directory name |
| `kakehashi` | `__APP_NAME_LOWER__` / `__PROTO_PACKAGE__` | Go import paths, proto packages, compose names |
| `KAKEHASHI` | `__APP_NAME_UPPER__` | environment variable prefix |
| `SekiroKenjii` | part of `__GO_MODULE__` | `server/go.mod` and every Go import |
| `架け橋` | — | `README.md` and `docs/brand/` only; never reaches a scaffolded project |
| `#C4513C` | `__ACCENT__` | `docs/brand/` only. The client takes its accent from the system |

The one accent hardcoded outside `docs/brand/` is `#C42B1C` on the toggles in
`RolePermissionsPage.xaml`. It is not a brand value and the file is EXAMPLE, so it leaves with unit
`admin-ui` rather than becoming a placeholder.

## Decisions

| # | Question | ADR |
| --- | --- | --- |
| D1 | Where do Activity, the RBAC UI and the navigation admin go? | [0016](adr/0016-one-example-module-in-the-template.md) |
| D2 | Is the self-hosted OIDC provider CORE or optional? | [0017](adr/0017-oidc-provider-is-core.md) |
| D3 | Database-driven navigation, or static? | [0018](adr/0018-database-driven-navigation-stays.md) |
| D4 | Does the CLI live in this repository or its own? | [0019](adr/0019-cli-lives-in-the-monorepo.md) |
| D5 | A second example module for the event bus and Mongo? | [0020](adr/0020-no-second-example-module.md) |
