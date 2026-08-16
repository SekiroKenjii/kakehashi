# The three gates

A modular monolith is only modular while something checks. Two of these check inside a half; the
third checks the boundary between them, which is the one a compiler cannot see at all.

None is optional, and all three run on every push.

| Gate | Protects | Command |
| --- | --- | --- |
| `archlint` | module boundaries inside the Go server | `cd server && go run ./tools/archlint` |
| `<App>.ArchitectureTests` | the three layers inside the WinUI client | `cd client && dotnet test <App>.slnx` |
| `buf breaking` | the wire contract between them | `buf breaking --against '.git#branch=main'` |

---

## `archlint`

Reads the server's import graph with `go list` and fails on any edge that breaks a rule. It knows
packages, never files, so it is fast and it cannot be argued with.

**Green** looks like this:

```text
archlint: 132 packages, no boundary violations
```

**Red** names the edge, not the file:

```text
archlint: 1 boundary violation(s)

  example.com/app/internal/modules/orders/service
      imports example.com/app/internal/modules/notes/store
      module "orders" may only reach module "notes" through its api package
```

### The rules

1. A module may reach another module **only** through its `api` package.
2. An `api` package may not import another module at all, not even another `api` — contracts that
   reference each other are a cycle.
3. The platform may not import a module. Dependencies point inward.
4. The kernel (`internal/app`) may not import a module. Only `cmd/` mounts modules.
5. Inside a module, only `store/` may import `platform/database` or `platform/mongodb`.
6. Only `rpc/` may import `internal/gen`. Generated types are the wire's shape, not the module's.
7. Only the `account` module may import an OpenID Connect or JWT library.

### How to fix each

**Rule 1** — you wanted something another module owns. Either it belongs in that module's `api`
package (an interface, a DTO, an event), or the two modules should be collaborating through an event
on the bus instead of through a function call. Widening `api` is the honest answer surprisingly
often; reaching into `store` never is.

**Rule 2** — two contracts referencing each other means the boundary is in the wrong place. Move the
shared type to whichever module owns the concept, or to the platform if neither does.

**Rules 3 and 4** — something generic grew a specific dependency. The platform and the kernel exist
to be used by modules; the moment either knows a module's name, every module has to ship with them.

**Rule 5** — a service reached for SQL directly. Put the query in `store/` behind the port the
service already has.

**Rule 6** — a protobuf type escaped `rpc/`. Map it to an `api` type at the boundary. This is the
rule that keeps a wire-format change from becoming a business-rule change.

**Rule 7** — an OIDC or JWT library was imported outside `account`. Token handling lives in one
module on purpose.

Rules are data, in `check()` in `server/tools/archlint/main.go`, and each has a test in both
directions. Adding one there beats trusting everybody to remember it.

---

## `<App>.ArchitectureTests`

The client's half, as xUnit tests over assembly references rather than as a separate tool. Reflection
is enough: a project reference is an assembly reference, and layering is exactly a statement about
those.

```text
Domain       →  SharedKernel only
Application  →  Domain + SharedKernel + Application.Abstractions
UI (host)    →  Application + Domain + SharedKernel + WinUI/host libs
```

**Red** looks like an ordinary assertion failure naming the reference:

```text
Assert.DoesNotContain() Failure: Filter not matched
Collection: ["App.Orders.Domain", "App.SharedKernel", "App.Orders.Application"]
```

— the Domain project referencing Application, in that case.

### The rules

1. Modules never reference other modules. Cross-module collaboration is
   `IPublisher.Publish(INotification)`.
2. Application defines ports; concrete adapters live in the UI layer and are wired in
   `IModule.RegisterServices`.
3. Domain never throws for expected failures — it returns `Result` / `Result<T>`.
4. DTOs cross the Application boundary. A domain entity never reaches the UI.
5. `SharedKernel` has no `<App>.*` dependencies.
6. Application and Domain never reference the generated contract or gRPC. The client-side statement
   of the server's rule 6.

**Per-module layering lives with its module** — `OrdersLayeringTests` beside the Orders module — so
adding or removing one never means editing a shared file. `add module` writes it; `remove module`
takes it away.

### How to fix

Nearly every failure here is a project reference added to make one type resolve. Ask which layer the
type belongs to first: a DTO belongs to Application, an entity to Domain, anything that knows about
gRPC or WinUI to UI. If a module needs to know something happened in another, publish a notification
— that is what the mediator is for, and it is the only cross-module edge that exists.

---

## `buf breaking`

Compares `proto/` against the same directory on `main` and fails on a change that would break a
client already in the field. That last part is why it matters more here than in a web application: a
desktop client runs the version its user installed, for as long as they like.

```sh
buf lint                                              # style, naming, package layout
buf breaking --against '.git#branch=main'             # compatibility
buf generate && git diff --exit-code -- server/internal/gen
```

The third line is a gate too. The generated tree is committed so a fresh clone builds without buf
installed, and that only stays honest if regenerating changes nothing.

**Red** names the field:

```text
proto/app/orders/v1/orders.proto:12:3: Field "3" with name "customer" on message "Order"
changed type from "string" to "int64".
```

### What is safe, what is not

| | |
| --- | --- |
| **Safe** | adding a message, a field, an enum value, a service method |
| **Safe** | adding a new `v2` package beside `v1` |
| **Breaking** | removing or renaming a field, a message, a method |
| **Breaking** | changing a field's type or its number |
| **Breaking** | reusing a number a deleted field had |

Field numbers are permanent. To retire a field, stop populating it and `reserved` its number so
nothing can reuse it. To change a type, add a new field with a new number and deprecate the old one.

A genuinely breaking change means a new package version — `orders/v2` beside `orders/v1` — and both
served until the old clients are gone. [CONTRACTS.md](CONTRACTS.md) is the longer version.

### When it is not your fault

`buf breaking` compares against `main`. A branch cut before a change landed there can report one it
did not make; merging `main` in first is the fix. On a push to `main` the comparison is against the
commit just pushed, which can never fail — which is why CI only runs it on pull requests.

---

## Running all three

```sh
buf lint && buf generate && git diff --exit-code -- server/internal/gen
cd server && go build ./... && go test ./... && go vet ./... && go run ./tools/archlint
```

```pwsh
cd client
dotnet build <App>.slnx                                       # zero errors, zero warnings
dotnet test  <App>.slnx
dotnet format <App>.slnx --verify-no-changes --severity warn
```

The Home page lists the three with a copy button on each, which is the shortest path from installing
the project to having run them once.
