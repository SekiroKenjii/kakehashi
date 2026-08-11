# Kakehashi — Claude Instructions

A WinUI 3 client and a Go backend in one repository, joined by contracts the build enforces. Both
halves are modular monoliths. Read `docs/ARCHITECTURE.md` before making a structural change.

## How to work — agent behavior

> These four rules address the most common LLM coding mistakes.
> They override any instinct to be thorough, helpful, or proactive beyond the request.

**Tradeoff:** These bias toward caution over speed. Use judgment for trivial one-line fixes.

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

Write the smallest change that satisfies the request. Nothing more.

- No features beyond what was asked.
- No new pipeline behaviors, interceptors, base classes, or abstractions for single-use code.
- No "configurability" that was not requested.
- No error handling for scenarios the architecture already prevents.
- If the result is 200 lines and 50 would work, rewrite it.

*Ask: "Would a senior engineer say this is overcomplicated?" If yes, simplify.*

### 3. Surgical changes

Touch only what the request requires.

- Do not improve adjacent code, comments, imports, or doc comments.
- Do not refactor things that are not broken.
- Match existing style exactly — Google C# in `client/`, gofmt in `server/`.
- If you notice pre-existing dead code or a style issue, **mention it — do not fix it silently**.

**Your orphans are your responsibility:** remove imports, variables, handlers and registrations that
*your* change made unused. Leave pre-existing dead code alone unless asked.

### 4. Verifiable execution

Define success before writing code. For multi-step tasks, state the plan first:

```text
1. [Step] → verify: [command or test that proves it]
2. [Step] → verify: [...]
```

**The gates. All must be green before a task is done.**

```sh
# Contract
buf lint && buf generate && git diff --exit-code -- server/internal/gen

# Server
cd server && go build ./... && go test ./... && go vet ./... && go run ./tools/archlint
```

```pwsh
# Client
cd client
dotnet build Kakehashi.slnx                                       # zero errors, zero warnings
dotnet test  Kakehashi.slnx                                       # all suites incl. architecture
dotnet format Kakehashi.slnx --verify-no-changes --severity warn  # no formatting drift
```

---

## Repository layout

```text
proto/          the contract. buf.yaml / buf.gen.yaml at the root.
server/         Go modular monolith → one static binary
client/         WinUI 3 modular monolith → .exe or MSIX
docs/           ARCHITECTURE.md, CONTRACTS.md, RBAC.md, NAVIGATION.md
```

## The three gates

| Gate | Protects | Never skip because |
| --- | --- | --- |
| `archlint` | server module boundaries | it is the only thing standing between "modular monolith" and "monolith with directories in it" |
| `Kakehashi.ArchitectureTests` | client layering | same, for the other half |
| `buf breaking` | the wire contract | a desktop client runs the version the user installed, for as long as they like |

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

### Decomposition — one file per unit, and the unit differs per package

The package list above is where a module's decomposition *starts*, not where it ends. Inside a
module you are free to design, and that freedom comes with the obligation to use it: a package
whose whole content sits in one file called `service.go` or `sqlserver.go` has not been designed,
it has been accumulated.

**Split every package along that package's own unit, and name each file after the unit it holds.**
The axis differs per package, because each layer draws a different boundary:

| Package | The unit | Seam file holds | Example |
| --- | --- | --- | --- |
| `domain/` | an **aggregate root** — one consistency boundary | `doc.go`, once there is more than one root | `account/domain/session.go` holds `UserSession` *and* the `IssuedToken` inside it |
| `store/` | a **table or collection** | `store.go` — the type, `New`, and the scan/param helpers | `account/store/token.go` exists though `IssuedToken` is no root: it is its own table |
| `service/` | a **use-case family** — the methods one caller reaches for together | `service.go` — package doc, the `Store` port, `Clock`/`IDs`, the type, `New`, the `var _` assertion | `account/service/signin.go` is exactly what `accountapi.Service` withholds |
| `rpc/` | an **externally visible surface** — one handler, one route group, one third-party interface | `provider.go` or `rpc.go` — the wire assembly and the route table | `account/rpc/oidc_storage.go` is one `op.Storage`, at 450 lines |
| `api/` | the **module**. One `api.go`, and it stays one file | itself | splitting it would publish the decomposition the package exists to hide |

That `store/` and `domain/` disagree about `IssuedToken` is the point, not an inconsistency:
persistence and the domain are allowed to draw different lines, and the unit is per-package rather
than global.

**Helper placement**, in this order:

1. An unexported helper lives with its only caller.
2. A helper with callers in more than one file lives with the unit it operates on.
3. A helper that operates on no unit lives on the seam.

That is why `storable`, `nullable` and `requireOneRow` sit in `store/store.go`, while
`service/record` — five call sites across three files — sits in `securityevent.go` beside the read
it feeds.

### When *not* to split

More important than the rule, and the reason it is not "keep files under N lines".

**Split only when the package's axis has two or more values and a reader routinely needs one
without the others.** Below that a split is a rename. Never cut a thing whose value is its
**order** (a migration history) or its **wholeness** (a public contract, a handler and the template
it serves).

Size is not the test in either direction:

| File | Lines | Verdict |
| --- | --- | --- |
| `account/service/ids.go` | 12 | correct — one unit |
| `notes/service/service.go` | 165 | correct — one aggregate, one family, nothing to cut |
| `account/store/migrations.go` | 143 | correct — its unit is the ordered history; migration 2 only reads under migration 1 |
| `account/rpc/oidc_storage.go` | 450 | correct — one implementation of one library's interface |
| `account/service/service.go` | 308 | **wrong**, and now split — five families in one file |

Go lets a type's methods live in any file of a package, so "the library wants one implementation"
is a claim about the **type**, never about the **file**. Every `var _ Iface = (*T)(nil)` keeps
compiling after a cut.

**The review test:** can you name the file after the one thing it holds, without using the word
"and" and without repeating the package name? `sqlserver.go`, `types.go`, and a `service.go`
holding everything are what a file gets called when its contents refuse a name. A second, free
signal: where a file's own banner comments name different nouns, the author already found the seam
and then did not cut along it.

**This is a review convention, not tool-enforced.** archlint's data model is the import graph — it
knows packages, never files — and every proxy for this rule (a line-count cap, a mandatory
`doc.go`) would fire on the files in the table above that are already right. Do not add one.

### archlint rules — hard CI failures

1. A module may reach another module **only** through its `api` package.
2. An `api` package may not import another module at all, not even another `api`.
3. The platform may not import a module.
4. The kernel (`internal/app`) may not import a module. Only `cmd/` mounts modules.
5. Inside a module, only `store/` may import `platform/database` or `platform/mongodb`.
6. Only `rpc/` may import `internal/gen`.
7. Only the `account` module may import an OpenID Connect or JWT library.

Access control is not an archlint rule, because it is not an import-graph fact. It is structural
instead, in two layers.

**Every route states its own policy** — `app.Public()`, `app.SignedIn()`, `app.ModuleAccess()` or
`app.Permission(key)` — beside its pattern, and the kernel refuses at boot to collect a route that
states none. `RoutePolicy` has no exported literal form, so the zero value cannot be mistaken for a
decision; it means "unset", and boot says so with the module and the pattern. The mux enforces from
the declaration, which is why no module.go wraps its own handler in a permission check any more:
the wrapper somebody forgets is the breach.

**The composition root still names who may be unprotected.** `unprotectedRouteModules` in
`cmd/server/main.go` lists the modules permitted to declare `Public()` or `SignedIn()` at all, and
boot refuses any other module that tries. Granularity lives on the route, review salience lives at
the root: a module that could exempt itself would opt out by editing one line of its own file, and
the documented way to add a module is to copy an existing one. The list grows per security
exemption, never per module, and `cmd/server/main_test.go` asserts it stays honest with no database.

The module is called `account`, not `identity`, because the module ID doubles as the SQL schema
name and `IDENTITY` is a reserved T-SQL word. Same reason its tables are `Account` and
`UserSession` rather than `User` and `Session`.

Rules are data, in `check()` in `server/tools/archlint/main.go`. Add one there rather than trusting
everyone to remember. Every rule has a test in `main_test.go`, in both directions.

### Errors

Return `*errs.Error` from `platform/errs`, never a bare `errors.New` for an expected failure. The
interceptor in `platform/rpc` maps the `Kind` to a Connect code and hides the message of anything
`Internal`. **Handlers never build a `*connect.Error` themselves** — a service that chooses status
codes has started knowing it is on a network.

Reserve panics for programmer errors. `app.Use[T]` panicking at boot is deliberate.

### Activity kinds, and writes that come from a client

Two rules, both about the activity feed, and neither is a style preference.

**A kind is named in exactly one place: `activity/api/api.go`.** The constant, the category it maps
to, and whether a client may report it all live in that file. Adding a kind means editing it and the
publisher — nothing else, and never a second table in the client. Categories are the server's because
counts are over everything retained, which only the server can see.

**A write path from a client goes through a closed allow-list or it does not exist.**
`RecordClientEvent` is the only one, and `activityapi.CanReport` is the list. Anything a caller could
choose is decided by the server instead: the subject comes from the token, the timestamp from the
server's clock, the device and address from the connection. The refusal names no kinds — listing what
is allowed teaches a caller what else to try.

Extending that list is a security decision. Answer "could a compromised client use this to tell a lie
a reader would act on?" before adding to it, and read `docs/ACTIVITY.md` first.

### Storage rules

- **Each module owns a SQL schema named after its module ID** (`notes.Note`). `Migrate` creates it
  before the first migration runs. Writing outside your own schema has to be spelled out in the
  SQL, where a reviewer can see it.
- SQL Server uses `@p1`, `@p2` placeholders, **not** `?`.
- There is no `LastInsertId`. Use `OUTPUT INSERTED.Id`.
- Migrations are append-only. **Never edit one that has shipped** — add another.
- Mongo has no migrations, only indexes, declared through the `Indexer` interface.

### SQL style — [ktaranov/sqlserver-kit](https://github.com/ktaranov/sqlserver-kit)

Not optional, and not negotiable by taste. The rules that bite most often:

| Rule | Right | Wrong |
| --- | --- | --- |
| Objects are PascalCase and singular | `notes.Note` | `notes_note`, `Notes` |
| No square brackets, no reserved words | `notes.Note AS n` | `[dbo].[notes_note]` |
| Always schema-qualify | `notes.Note` | `Note` |
| Data types lower-case | `nvarchar(120)`, `datetime2(3)` | `NVARCHAR(120)` |
| Keywords UPPERCASE | `SELECT`, `INSERT INTO` | `select` |
| Block comments only | `/* why */` | `-- why` |
| Explicit column lists | `SELECT n.Id, n.Title` | `SELECT *` |
| Table aliases, always | `FROM notes.Note AS n` | `FROM notes.Note` |
| Spaces, never tabs, inside SQL strings | | |
| Semicolon on every statement | | |

Constraint and index names: `PK_<Table><Column>`, `FK_<Table>_<ForeignTable><Column>`,
`AK_<Table>_<Column>` for unique keys, `IX_<Table>_<Column>...` for non-clustered indexes,
`DF_<Table>_<Column>` for defaults, `CK_<Table>_<Column>` for checks.

**Check names against the reserved-word list before choosing them.** `IDENTITY`, `USER`, `KEY` and
`SESSION` are all taken; a name that needs brackets to parse is the wrong name.

### Adding a module

1. Copy `internal/modules/notes/` → `internal/modules/<id>/`, rename the package and the ID.
   (`health/` is the same shape minus `domain/` and `store/`, for a module that stores nothing;
   `activity/` is the one to copy for MongoDB, and for a module that reacts to another's events.)
2. Add `proto/kakehashi/<id>/v1/<id>.proto`, run `buf generate`, commit the output.
3. Mount it in `cmd/server/main.go` — one line, the only file that names it.
4. **Name the units.** Before writing the second use case, list this module's aggregate roots, its
   tables and its use-case families, and give each one a file. `notes/` is a one-root module and
   one file per package is right for it — copying its *shape* is correct, copying its *file count*
   is not. See [Decomposition](#decomposition--one-file-per-unit-and-the-unit-differs-per-package),
   and add a `domain/doc.go` naming the roots the moment there is a second one.
5. `go run ./tools/archlint` must be green before committing.

---

## Client (WinUI 3)

### Stack

| Concern | Choice |
| --- | --- |
| Host | WinUI 3 / `Microsoft.WindowsAppSDK` 2.1.x, .NET 10, C# `latest` |
| MVVM | `CommunityToolkit.Mvvm` 8.4.x — source generators **on** |
| DI / hosting | `Microsoft.Extensions.Hosting` + `DependencyInjection` |
| Mediator | custom in-process mediator (`Kakehashi.Mediator`) — **no MediatR** |
| Backend transport | `Grpc.Net.Client` + `Grpc.Net.ClientFactory`, generated from `proto/` at build time |
| Win32 interop | `Microsoft.Windows.CsWin32` via `NativeMethods.txt`, never `[DllImport]` |
| Testing | **xUnit v3** + **NSubstitute** — no Fluent Assertions, no MediatR mocks |

### Layering — enforced by `Kakehashi.ArchitectureTests`

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
5. `SharedKernel` has no `Kakehashi.*` dependencies.

Per-module layering lives with its module (`AuthLayeringTests`), so adding or removing a module
never means editing `LayeringTests`.

### Patterns

**View models** extend `ViewModel`, are `partial`, and use `[ObservableProperty]` / `[RelayCommand]`
source generators. Never hand-write `INotifyPropertyChanged`. Inject `ISender`, never `IMediator`.

**XAML** uses compiled `x:Bind`, not `{Binding}`.

**Result** carries expected domain failures; exceptions are for programmer errors.

**Packages**: every version is pinned in `client/Directory.Packages.props`. Never put `Version=""`
in a `.csproj`. Forbidden (relicensed/paid): Fluent Assertions, MediatR, AutoMapper.

### Style

`.editorconfig` in `client/` encodes the Google C# Style Guide and is `root = true`, so it does not
leak onto Go or proto files. 2-space indent (4 in XAML), 100-column limit, `using` outside
namespaces with `System.*` first, no `this.`, no implicit usings, warnings as errors.

Member order (review convention, not tool-enforced): nested types → static/const/readonly fields →
fields and properties → constructors → methods; public before non-public within each group.

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
models are tested with substituted services; **never construct XAML controls** (`Page`, `Frame`) —
anything needing the UI thread stays an integration concern. Unregister `WeakReferenceMessenger`
recipients on teardown.

Integration tests wire the real mediator with an in-memory repository. Do not mock the mediator.
