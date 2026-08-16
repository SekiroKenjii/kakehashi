# Your first module

`kakehashi add module orders` writes a feature across both halves and every line that wires it in.
This is what it writes, and how to turn it into something that does your work.

Run it from anywhere inside the project.

```sh
kakehashi add module orders
```

`--dry-run` prints the plan and writes nothing. Read that first if you would rather see the file
list before it exists.

## What it wrote

```text
proto/<pkg>/orders/v1/orders.proto            the contract
server/internal/gen/<pkg>/orders/v1/          what buf generates from it (committed)
server/internal/modules/orders/
  api/api.go                                  the only package other modules may import
  domain/order.go                             the aggregate and its invariants
  store/order.go                              orders.Order, in the module's own SQL schema
  service/                                    the use cases
  rpc/                                        the wire; the only package that may import gen
  module.go                                   the wiring
client/src/Modules/Orders/
  …Orders.Domain/                             entities, Result, no dependencies
  …Orders.Application/                        commands, queries, handlers, the gateway port
  …Orders.UI/                                 the page, its view model, the gRPC adapter
client/tests/…                                one suite per layer, plus OrdersLayeringTests
.kakehashi/units/orders.json                  what removal reads to take it back out
```

And the wiring, inside the marker fences that make it removable: `server/cmd/server/main.go`, the
solution file, `ModuleCatalog.cs`, the host `.csproj`, the architecture-test project.

**The naming is derived from the id.** `orders` gives the package, the SQL schema and the proto
directory; `Orders` the namespace segment; `Order` the entity. For a word English does not inflect
that way, `--entity` says it outright: `kakehashi add module people --entity Person`.

## Check it before you touch it

The point of the generator is that all three gates are green the moment it finishes.

```sh
buf lint && buf generate && git diff --exit-code -- server/internal/gen
cd server && go build ./... && go test ./... && go run ./tools/archlint
```

```pwsh
cd client && dotnet build <App>.slnx && dotnet test <App>.slnx
```

Run the app: the new module is in the navigation pane, its page lists nothing, and the Create button
writes a row. Now make it yours.

## 1. Change the contract first

Everything else follows from `proto/<pkg>/orders/v1/orders.proto`. Add the fields the entity really
has:

```proto
message Order {
  int64 id = 1;
  string reference = 2;
  string customer = 3;
  int32 quantity = 4;         // new
  google.protobuf.Timestamp placed_at = 5;
}
```

Field numbers are permanent. Adding one is safe; renumbering or reusing one is what `buf breaking`
exists to refuse — see [CONTRACTS.md](CONTRACTS.md).

```sh
buf lint
buf generate
git diff --exit-code -- server/internal/gen   # now fails: regenerate and commit
```

Commit the generated code. A fresh clone has to build without buf installed, which is why that tree
is in the repository rather than in `.gitignore`.

## 2. Teach the domain the rule

`server/internal/modules/orders/domain/order.go` is where an invariant belongs — not in the handler,
not in the page.

```go
// NewOrder refuses an order for nothing, which is the one thing a quantity may not be.
func NewOrder(reference, customer string, quantity int32) (*Order, error) {
    if quantity <= 0 {
        return nil, errs.Invalid("quantity", "an order is for at least one thing")
    }
    ...
}
```

Return `*errs.Error` from `platform/errs`, never a bare `errors.New`. The interceptor maps its
`Kind` to a Connect code and hides the message of anything `Internal`; a handler that builds a
`*connect.Error` itself has started knowing it is on a network.

## 3. Store it

Migrations are append-only. The generator wrote migration 1; the column goes in migration 2, and
migration 1 is never edited again — somebody has already run it.

```go
{Version: 2, Statements: []string{
    `ALTER TABLE orders."Order" ADD Quantity int NOT NULL
        CONSTRAINT DF_OrderQuantity DEFAULT 1;`,
}},
```

SQL Server, not PostgreSQL: placeholders are `@p1`, there is no `LastInsertId` — use
`OUTPUT INSERTED.Id` — and objects are PascalCase, singular and schema-qualified. The full style is
in [CLAUDE.md](../CLAUDE.md).

## 4. Carry it across

The client mirrors the same slice, and the compiler walks you through it: `OrderDto`,
`CreateOrderCommand`, the handler, `IOrdersGateway`, `GrpcOrdersGateway`, the view model, the page.

Two rules the tests enforce rather than trust:

- **Domain never throws for an expected failure.** Return `Result` / `Result<T>`. Exceptions are for
  programmer errors.
- **DTOs cross the Application boundary.** A domain entity never reaches the UI.

## 5. Add a page to the module

```sh
kakehashi add page orders Archive
```

Client only: `ArchivePage`, `ArchiveViewModel`, the registration and a navigation entry — inside the
module's own marker fences, so `remove module orders` still takes everything.

`--no-nav` registers the page without giving it an entry in the pane, for one you navigate to from
somewhere else.

## What not to do

**Do not reference another module.** On the server, reach a module only through its `api` package;
`archlint` fails the build otherwise. On the client, modules do not reference each other at all —
they collaborate through `IPublisher.Publish(INotification)`.

**Do not import the generated code outside `rpc/`.** That package exists so that a change to the
wire format is not a change to your business rules.

**Do not hand-edit inside a marker fence,** and never delete one. The fences are how
`remove module` knows what to take back; a fence somebody deleted is wiring that outlives the
module.

**Do not edit a migration that has shipped.** Add another one.

## When you no longer want it

```sh
kakehashi remove module orders
```

It reads `.kakehashi/units/orders.json` — the record the generator left — and takes back exactly
what went in. See [remove-example.md](remove-example.md).
