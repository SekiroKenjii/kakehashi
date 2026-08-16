# __APP_TITLE__ — Claude Instructions

A WinUI 3 client and a Go backend in one repository, joined by contracts the build enforces. Both
halves are modular monoliths. Read `docs/ARCHITECTURE.md` before making a structural change.

The scaffold's own record — the template version and the answers it was made from — is
`.kakehashi.json`.

## How to work — agent behavior

### 1. Think before coding

Before writing a single line, surface what you do not know.

- **State assumptions explicitly.** If uncertain, ask — never pick silently.
- **Name the half and the layer** the change lives in (server: `platform` / `app` / a module's
  `api`, `domain`, `store`, `service`, `rpc` — client: Domain / Application / UI). If it is
  ambiguous, stop and say so.
- **Flag boundary crossings.** Server modules couple only through `api` packages and the event bus;
  client modules only through `IPublisher.Publish(INotification)`. Never a direct reference. Ask
  before introducing one.
- **A change to `proto/` is a change to both halves.** Say so, and say whether it is breaking.
- **For WinUI changes**, state whether the code runs on the UI thread or a background thread.
  Crossing requires `DispatcherQueue.TryEnqueue`.

### 2. Minimum code

Write the smallest change that satisfies the request. Nothing more. No features beyond what was
asked, no abstractions for single-use code, no configurability that was not requested, no error
handling for scenarios the architecture already prevents.

*Ask: "Would a senior engineer say this is overcomplicated?" If yes, simplify.*

### 3. Surgical changes

Touch only what the request requires. Do not improve adjacent code, comments or imports; do not
refactor what is not broken. Match the existing style exactly — C# in `client/`, gofmt in `server/`.
If you notice pre-existing dead code, **mention it — do not fix it silently**.

**Your orphans are your responsibility:** remove imports, variables, handlers and registrations that
*your* change made unused.

### 4. Comments

State facts, not narrative. Present tense, current code only — history belongs in the pull request
or an ADR. No doc comment on a member whose name already says everything. A comment block over six
lines belongs in `docs/adr/`. Never quote an old comment inside a new one.

### 5. Verifiable execution

Define success before writing code. For a multi-step task, state the plan first, each step with the
command that proves it.

---

## The gates. All must be green before a task is done.

```sh
# Contract
buf lint && buf generate && git diff --exit-code -- server/internal/gen

# Server
cd server && go build ./... && go test ./... && go vet ./... && go run ./tools/archlint
```

```pwsh
# Client
cd client
dotnet build __APP_NAME__.slnx                                       # zero errors, zero warnings
dotnet test  __APP_NAME__.slnx                                       # all suites incl. architecture
dotnet format __APP_NAME__.slnx --verify-no-changes --severity warn  # no formatting drift
```

| Gate | Protects | Never skip because |
| --- | --- | --- |
| `archlint` | server module boundaries | it is the only thing standing between "modular monolith" and "monolith with directories in it" |
| `__APP_NAME__.ArchitectureTests` | client layering | same, for the other half |
| `buf breaking` | the wire contract | a desktop client runs the version the user installed, for as long as they like |

`docs/gates.md` explains what each refuses and how to read what it prints.

---

## Repository layout

```text
proto/          the contract. buf.yaml / buf.gen.yaml at the root.
server/         Go modular monolith → one static binary
client/         WinUI 3 modular monolith → .exe or MSIX
docs/           ARCHITECTURE.md, CONTRACTS.md, RBAC.md, NAVIGATION.md, gates.md, cli.md
```

## Generator markers — do not hand-edit them away

Lines fenced by a pair such as `kakehashi:module-registrations:begin` and its `:end` are written
and taken back by `kakehashi add module` and `kakehashi remove module`. They appear in the
composition roots — `server/cmd/server/main.go`, `client/__APP_NAME__.slnx`, `ModuleCatalog.cs`,
the host `.csproj` — and in each module for its own pages.

- **Add wiring for a new module by running the command**, not by typing inside the fences.
- **Never delete a fence.** A removed marker is wiring the CLI can no longer take back, and the next
  `remove module` leaves a project that does not compile.
- Anything outside the fences is yours.

---

## Server (Go)

### Stack

| Concern | Choice |
| --- | --- |
| Language | Go 1.26 |
| Transport | `connectrpc.com/connect` — gRPC, gRPC-Web and JSON on one `net/http` mux |
| Codegen | `buf` + `protoc-gen-go` + `protoc-gen-connect-go`, output committed under `server/internal/gen` |
| Transactional store | SQL Server via `github.com/microsoft/go-mssqldb` |
| Document store | MongoDB via `go.mongodb.org/mongo-driver/v2` |
| Identity | `github.com/zitadel/oidc/v3` — the server is its own OpenID Connect provider |
| Observability | OpenTelemetry, OTLP out, configured by the standard `OTEL_*` variables |

### Module anatomy

```text
internal/modules/<id>/
  api/        interfaces, DTOs, events. The only package other modules may import.
  domain/     entities and invariants. No SQL, no protobuf.
  store/      persistence. Tables live in the module's own SQL schema (`<id>.Thing`);
              Mongo collections are prefixed `<id>_`.
  service/    use cases. Orchestrates domain and store; publishes events.
  rpc/        the wire. The only package that may import internal/gen.
  module.go   the wiring.
```

**One file per unit, and the unit differs per package.** `domain/` splits by aggregate root,
`store/` by table or collection, `service/` by use-case family, `rpc/` by externally visible
surface. `api/` stays one file. Split only when a package's axis has two or more values and a reader
routinely needs one without the others — below that a split is a rename. Never cut a thing whose
value is its **order** (a migration history) or its **wholeness** (a public contract).

### archlint rules — hard CI failures

1. A module may reach another module **only** through its `api` package.
2. An `api` package may not import another module at all, not even another `api`.
3. The platform may not import a module.
4. The kernel (`internal/app`) may not import a module. Only `cmd/` mounts modules.
5. Inside a module, only `store/` may import `platform/database` or `platform/mongodb`.
6. Only `rpc/` may import `internal/gen`.
7. Only the `account` module may import an OpenID Connect or JWT library.

Rules are data, in `check()` in `server/tools/archlint/main.go`. Add one there rather than trusting
everyone to remember.

**Access control is structural, not an import rule.** Every route states its own policy —
`app.Public()`, `app.SignedIn()`, `app.ModuleAccess()` or `app.Permission(key)` — beside its
pattern, and the kernel refuses at boot to collect a route that states none.
`unprotectedRouteModules` in `cmd/server/main.go` lists the modules permitted to declare `Public()`
or `SignedIn()` at all.

### Errors

Return `*errs.Error` from `platform/errs`, never a bare `errors.New` for an expected failure. The
interceptor in `platform/rpc` maps the `Kind` to a Connect code and hides the message of anything
`Internal`. **Handlers never build a `*connect.Error` themselves.** Reserve panics for programmer
errors.

### Storage rules

- **Each module owns a SQL schema named after its module ID** (`notes.Note`).
- SQL Server uses `@p1`, `@p2` placeholders, **not** `?`.
- There is no `LastInsertId`. Use `OUTPUT INSERTED.Id`.
- Migrations are append-only. **Never edit one that has shipped** — add another.
- Mongo has no migrations, only indexes, declared through the `Indexer` interface.

### SQL style — [ktaranov/sqlserver-kit](https://github.com/ktaranov/sqlserver-kit)

| Rule | Right | Wrong |
| --- | --- | --- |
| Objects are PascalCase and singular | `notes.Note` | `notes_note`, `Notes` |
| No square brackets, no reserved words | `notes.Note AS n` | `[dbo].[notes_note]` |
| Always schema-qualify | `notes.Note` | `Note` |
| Data types lower-case | `nvarchar(120)` | `NVARCHAR(120)` |
| Keywords UPPERCASE | `SELECT` | `select` |
| Block comments only | `/* why */` | `-- why` |
| Explicit column lists | `SELECT n.Id, n.Title` | `SELECT *` |
| Table aliases, always | `FROM notes.Note AS n` | `FROM notes.Note` |

Constraint and index names: `PK_<Table><Column>`, `FK_<Table>_<ForeignTable><Column>`,
`AK_<Table>_<Column>`, `IX_<Table>_<Column>...`, `DF_<Table>_<Column>`, `CK_<Table>_<Column>`.
Check names against the reserved-word list first: `IDENTITY`, `USER`, `KEY` and `SESSION` are taken.

---

## Client (WinUI 3)

### Stack

| Concern | Choice |
| --- | --- |
| Host | WinUI 3 / `Microsoft.WindowsAppSDK` 2.1.x, .NET 10, C# `latest` |
| MVVM | `CommunityToolkit.Mvvm` 8.4.x — source generators **on** |
| DI / hosting | `Microsoft.Extensions.Hosting` + `DependencyInjection` |
| Mediator | custom in-process mediator (`__APP_NAME__.Mediator`) — **no MediatR** |
| Backend transport | `Grpc.Net.Client` + `Grpc.Net.ClientFactory`, generated from `proto/` at build time |
| Win32 interop | `Microsoft.Windows.CsWin32` via `NativeMethods.txt`, never `[DllImport]` |
| Testing | **xUnit v3** + **NSubstitute** — no Fluent Assertions, no MediatR mocks |

### Layering — enforced by `__APP_NAME__.ArchitectureTests`

```text
Domain       →  SharedKernel only
Application  →  Domain + SharedKernel + Application.Abstractions
UI (host)    →  Application + Domain + SharedKernel + WinUI/host libs
```

1. Modules never reference other modules. Cross-module collaboration is `IPublisher.Publish`.
2. Application defines ports; concrete adapters live in the UI layer, wired in
   `IModule.RegisterServices`.
3. Domain never throws for expected failures — return `Result` / `Result<T>`.
4. DTOs cross the Application boundary. Never return a domain entity to the UI.
5. `SharedKernel` has no `__APP_NAME__.*` dependencies.

Per-module layering lives with its module (`AuthLayeringTests`), so adding or removing a module
never means editing `LayeringTests`.

### Patterns

**View models** extend `ViewModel`, are `partial`, and use `[ObservableProperty]` / `[RelayCommand]`
source generators. Never hand-write `INotifyPropertyChanged`. Inject `ISender`, never `IMediator`.

**XAML** uses compiled `x:Bind`, not `{Binding}`. A page shows a breadcrumb header, organises content
as caption-labelled cards, and pages long lists five rows at a time. Colours come from
`{ThemeResource ...}` — never a literal.

**Result** carries expected domain failures; exceptions are for programmer errors.

**Packages**: every version is pinned in `client/Directory.Packages.props`. Never put `Version=""`
in a `.csproj`. Forbidden (relicensed/paid): Fluent Assertions, MediatR, AutoMapper.

### Style

`.editorconfig` in `client/` is the style, and is `root = true`. 4-space indent, 120-column limit,
file-scoped namespaces with a blank line either side, `using` outside namespaces with `System.*`
first, no `this.`, no implicit usings, warnings as errors.

Braces are Allman for what declares or branches and K&R for what evaluates: accessors, lambdas,
anonymous methods and types, object/collection initializers, switch expressions, patterns.
`client/docs/csharp-style.md` states the whole rule.

Member order: nested types → static/const/readonly fields → fields and properties → constructors →
methods; public before non-public within each group.

The analyzers in `client/tools/__APP_NAME__.Analyzers` add four layout rules `dotnet format` has no
option for: a blank line before `return` and before `if`, blank lines around the namespace
declaration, chained calls one per line with the dot leading, and indentation in whole units of four.

### Platform notes

- WinUI 3 controls must be touched on the dispatcher thread. From background work use
  `DispatcherQueue.TryEnqueue`.
- The app defaults to **unpackaged**; `-p:Packaged=true` enables MSIX. Use `AppContext.BaseDirectory`
  rather than hardcoding paths.
- Building the WinUI `.csproj` directly needs an explicit `-p:Platform=x64`. Building the solution
  does not.

---

## Testing conventions

**Server.** Table-driven where it fits. Domain and service tests need no database; anything that
genuinely does is an integration test and is tagged as one. Every archlint rule has a test in both
directions.

**Client.** One test class per handler/entity, `sealed`. Substitutes as fields, SUT built in a
`CreateX()` factory. `Assert.*` only. `Received(n)` / `DidNotReceive()` for interactions. View
models are tested with substituted services; **never construct XAML controls** (`Page`, `Frame`).
Unregister `WeakReferenceMessenger` recipients on teardown.

---

## Commit messages — Conventional Commits, no exceptions

```text
<type>: <subject in the imperative, lower case, no full stop>

<body: why, not what. Wrap at 72 columns.>
```

| Type | For |
| --- | --- |
| `feat` | a capability the product did not have |
| `fix` | a defect somebody could hit |
| `refactor` | the behaviour is identical and the code is not |
| `docs` | documentation, comments, README, the docs/ tree |
| `test` | tests only |
| `chore` | tooling, scripts, dependencies, repository plumbing |
| `ci` | the workflow files |

Scope is optional and used sparingly: `feat(navigation):`. Breaking changes take `!` before the
colon and explain themselves in the body.

**Never write the `Co-Authored-By` trailer.**
