# Removing the example

The Notes module is one feature end to end. It is there to be read and then deleted, and deleting it
is a command rather than an afternoon.

```sh
kakehashi remove module notes
```

## What goes

Removal reads the unit record — `templates/units/notes.json` for a module the template shipped,
`.kakehashi/units/<id>.json` for one a generator wrote — and takes back exactly what that record says
went in.

**Whole paths:**

```text
proto/<pkg>/notes/v1/
server/internal/gen/<pkg>/notes/v1/
server/internal/modules/notes/
client/src/Modules/Notes/
client/tests/…Notes…                 four test projects
client/tests/…ArchitectureTests/NotesLayeringTests.cs
```

**And the wiring**, from between the marker fences in the files that know every module:

```text
server/cmd/server/main.go            the import and the registration
server/cmd/server/main_test.go       the module id
client/<App>.slnx                    the project entries
client/src/App/…/Composition/ModuleCatalog.cs
client/src/App/…/<App>.App.csproj
client/tests/…ArchitectureTests/…csproj
```

Nothing is left to find later. That is the difference between a removable unit and a module you
delete by hand.

## First, look

```sh
kakehashi remove module notes --dry-run
```

Prints the plan and removes nothing.

Removal also refuses to run in a working tree that has other changes in it, so that `git diff` after
it is the removal and nothing else. `--force` overrides that when you have a reason.

## Then check

All three gates, because removal touches both halves and the contract:

```sh
buf lint
cd server && go build ./... && go test ./... && go run ./tools/archlint
```

```pwsh
cd client && dotnet build <App>.slnx && dotnet test <App>.slnx
```

`buf breaking` will report the removed service as a breaking change, and it is one — that is the
right answer for a wire contract. Removing the example before anything has shipped is exactly when
that costs nothing, which is an argument for doing it early rather than for skipping the check.

## What is left

The frame, with no feature module: the shell, navigation, the account module and its OpenID Connect
provider, the activity feed, the three gates, the CI workflows. Identical to what
`kakehashi new --bare` produces — the same removal runs at scaffold time.

The Home page notices. Its checklist rows about the example are contributed by the example module
itself, so when the module is gone the rows are gone, and what remains is the backend row, the
architecture reading and "add your first module". A bare Home page is deliberate rather than empty.

## Before you do it

Notes is the only worked example in the repository of:

- a `proto` service with a full CRUD surface
- `api` / `domain` / `store` / `service` / `rpc` with something real in each
- the client's three layers over the same feature
- a page with a list, paging, and create/edit/delete dialogs
- one suite per layer, and a layering test

`kakehashi add module` writes the same shape whenever you want it back, so nothing is lost for good.
But if you are still learning the layout, read [first-module.md](first-module.md) with Notes open
beside it, and remove it afterwards.
