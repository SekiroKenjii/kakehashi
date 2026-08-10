# /new-module

Scaffold a new feature across both halves of Kakehashi: the contract, the Go module that serves it,
and the WinUI module that calls it. Follow every step in order; all three gates must be green before
calling the task done.

The reference implementation on both sides is `Notes`. Copy it rather than inventing a shape.

## Inputs

Ask the user for the module name if it was not given as an argument (e.g. `/new-module Inventory`).
It must be PascalCase for the client, lowercase for the server and the proto package, and must not
already exist in either half.

## Steps

### 1 — Define the contract

Create `proto/kakehashi/<id>/v1/<id>.proto` with package `kakehashi.<id>.v1`. Then:

```sh
buf lint && buf generate
git add server/internal/gen
```

The generated output is committed. CI regenerates and fails on any diff.

### 2 — Copy the server reference module

```sh
cp -r server/internal/modules/notes server/internal/modules/<id>
```

Rename the package and the `ID()`. `notes` is the full shape: `api/`, `domain/`, `store/`,
`service/`, `rpc/`. A module that stores nothing can drop `domain/` and `store/` — see `health` for
that stripped-down version.

Remember: tables live in the module's own SQL schema (`<id>.Thing`), which `Migrate` creates for
you; Mongo collections are prefixed `<id>_`; only `store/` may import the database packages; and
only `rpc/` may import `internal/gen`.

### 3 — Mount it

One line in `server/cmd/server/main.go`, which is the only file that names every module:

```go
func modules() []app.Module {
    return []app.Module{
        // ...
        <id>.New(),
    }
}
```

That is the whole composition-root change. Everything else about the module — its permissions, its
route policies, its screen — the module declares about itself, so this file does not grow in width
as the product does.

### 4 — Declare what protects it, and what it shows

Three things now live with the module rather than at the composition root:

```go
// Routes: every route states its policy beside its pattern. Boot refuses one that states none, and
// refuses Public()/SignedIn() unless main.go's unprotectedRouteModules names your module.
{Pattern: pattern, Handler: handler, Policy: app.ModuleAccess()}

// catalogue.go: the permissions your handlers check, if any beyond <id>.access.
// Set IsScoped only if one of YOUR stores narrows its query on auth.ScopeOf — it is a promise
// nothing can verify, and the administration screen offers the own/team/all picker on the strength
// of it.

// navigation.go: the screen your module owns, if it has one.
func (m *Module) NavigationDestinations() []navigationapi.Destination {
    return []navigationapi.Destination{{
        ID: "<id>", DefaultTitle: "<Name>", DefaultIcon: "<icon-name>",
        DefaultGroup: "utilities", DefaultOrder: 30,
    }}
}
```

Leave `Permission` empty and the destination is gated on `<id>.access`, the same permission the route
gate checks — so the pane locks exactly when the endpoints do. Name one explicitly (plus
`HideWhenDenied: true`) for an administrative screen that ordinary accounts should not even see
listed. A module whose routes are NOT gated on `<id>.access` must name a permission: nobody holds a
key no route checks, and boot refuses the declaration rather than drawing a row disabled forever.
See [docs/NAVIGATION.md](../../docs/NAVIGATION.md).

### 5 — Copy the client module

`client/src/Modules/Notes/` is the reference. Three projects under
`client/src/Modules/<Name>/`:

```
Kakehashi.Modules.<Name>.Domain        entities, invariants, Result
Kakehashi.Modules.<Name>.Application   commands/queries + handlers, ports, DTOs
Kakehashi.Modules.<Name>.UI            pages, view models, the gRPC adapter, IModule
```

The gateway port is declared in Application; the adapter that implements it with the generated gRPC
client lives in UI, registered from `IModule.RegisterServices`.

### 6 — Register the client module

- Add the three `.csproj` files, plus the test projects, to `client/Kakehashi.slnx`.
- Add a `<ProjectReference>` to the UI project in `client/src/App/Kakehashi.App/Kakehashi.App.csproj`.
- Add `new <Name>Module(),` to `client/src/App/Kakehashi.App/Composition/ModuleCatalog.cs`.
- Add `<Name>LayeringTests.cs` to `Kakehashi.ArchitectureTests`, mirroring `AuthLayeringTests`.
- Bump `_shippedModuleCount` in `HomePage.ViewModel.cs` if this module ships with the template.

### 7 — Verify

```sh
buf lint && buf generate && git diff --exit-code -- server/internal/gen
cd server && go build ./... && go test ./... && go run ./tools/archlint
```

```pwsh
cd client
dotnet build Kakehashi.slnx
dotnet test Kakehashi.slnx
dotnet format Kakehashi.slnx --verify-no-changes --severity warn
```

All must exit 0.

## Constraints

- Server: a module reaches another only through its `api` package. Client: only through
  `IPublisher.Publish(INotification)`. Never a direct reference in either half.
- Client dependency direction is always `UI → Application → Domain → SharedKernel`.
- New client packages are version-less; add the `<PackageVersion>` to
  `client/Directory.Packages.props` first.
- Do not add Fluent Assertions, MediatR, AutoMapper, or any relicensed library.
