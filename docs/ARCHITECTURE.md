# Architecture

Kakehashi is two modular monoliths with a contract between them. Each half is one deployable made
of modules that are as separate as if they were services; the contract is the one place they are
allowed to know anything about each other.

## Why the shape changed, and what stayed

The Go half descends from [gtk-boilerplate](https://github.com/SekiroKenjii/gtk-boilerplate), where
the whole application was a single process: UI, services and database compiled together, and a
module reached another module through its `api` package — a Go interface, resolved at compile time,
checked by `tools/archlint`.

Splitting the UI onto Windows and the services onto Linux breaks exactly one of those assumptions:
the two are no longer compiled together, so an interface cannot be the contract any more. Everything
else survives intact. The kernel, the staged boot, the service registry, the event bus, the
`api`-only rule and the linter that enforces it are all still here, doing the same job for the same
reason.

What replaces the interface is a `.proto` file. And what replaces `archlint` for *that* boundary is
`buf breaking`. The idea is unchanged — a contract nobody checks is a suggestion — only the tool is
different, because the boundary is.

## The three boundaries, and what guards each

| Boundary | Contract | Guard |
| --- | --- | --- |
| Between two server modules | the module's `api` package | `archlint` |
| Between two client modules | mediator notifications | `Kakehashi.ArchitectureTests` |
| Between client and server | `proto/` | `buf lint` + `buf breaking` |

None of the three is a convention. All three fail the build.

## The server

```text
cmd/server/main.go        composition root: knows every module, is known by none
    |
    v
internal/app/             the kernel: the Module contract, the registry, the HTTP mux
    |
    v
internal/modules/*/       the features
    |
    v
internal/platform/        config, logging, SQL Server, Mongo, event bus, errors, RPC options
```

Dependencies point downward, and only downward:

- `cmd/` may import anything. It is the only place that mounts modules.
- A module may import the platform, the kernel, and other modules' `api` packages.
- The platform may import nothing above it. It does not know modules exist.
- The kernel may not import a module either. It defines the contract they implement; it does not
  know who implements it.

### Anatomy of a module

```text
internal/modules/notes/
  api/        the contract: interfaces, DTOs, events. The only importable package.
  domain/     entities and the rules they enforce. No SQL, no protobuf.
  store/      persistence. Owns every table prefixed notes_.
  service/    use cases. Orchestrates domain and store; publishes events.
  rpc/        the wire. Maps api types to and from generated protobuf.
  module.go   the wiring.
```

`rpc/` is the one layer the desktop original did not have, and it exists for the same reason `api/`
does. A DTO in `api/` keeps other modules from compiling against your entities. `rpc/` keeps the
whole module from compiling against the wire format. Let a generated protobuf type into `service/`
and renaming a field in the schema becomes a change to a use case; keep it in `rpc/` and it is a
change to one mapping function.

The rule is checked: only `rpc/` may import `internal/gen`.

### Inside a package: the unit is not the same in every layer

Those six directories are where a module's decomposition starts. They are not where it ends, and
treating them as the whole answer is how the `account` module ended up with a 762-line `store` and
a 308-line `service` — six packages, correctly separated, each of them internally a heap.

The fix is one file per unit. What is interesting is that **the unit is different in every
package**, because each layer draws its boundary for a different reason:

- `domain/` splits by **aggregate root** — the consistency boundary. `UserSession` and the
  `IssuedToken` inside it share a file, because a token has no life without the session that issued
  it and ending the session must end the token. Give the token its own file and it starts to look
  like a root, and the first consequence is code that deletes a token without touching its session.
- `store/` splits by **table** — and gives `IssuedToken` a file, because it *is* its own table with
  its own inserts and deletes. That `store/` and `domain/` disagree about the same type is the
  clearest evidence the unit is per-package rather than global. Persistence and the domain are
  allowed to draw different lines; pretending otherwise is how ORMs get their reputation.
- `service/` splits by **use-case family** — which caller reaches for these together. Not by root,
  because a service *orchestrates* roots: half of `account`'s use cases touch two or more, so
  mirroring `domain/` would force an arbitrary "primary root" ruling on most of the package. The
  seam that actually exists in the code is the interface: `accountapi.Service` declares seven
  methods, `*service.Service` has three more, and those three are exactly the sign-in path. One
  file, `signin.go`, is the precise complement of the module's public contract.
- `rpc/` splits by **wire surface** — one handler, one route group, one third-party interface. The
  axis is the protocol, never the domain, which is why a 450-line `op.Storage` adapter reading six
  tables is one correct file.
- `api/` does not split at all. It exists to hide the module's internal decomposition, so
  publishing that decomposition as filenames would defeat the package.

The counter-rule matters more than the rule, because a codebase that splits by line count is worse
than one that never splits: it cuts migration histories into six files where the reading order was
the artifact, and leaves the actual heaps alone because they happened to be short. Split when the
axis has two or more values and a reader routinely needs one without the others. Below that, a
split is a rename.

The test a reviewer can apply without running anything: *name the file after the one thing it
holds, without using "and" and without repeating the package name.* `sqlserver.go` and `types.go`
are what a file gets called when its contents refuse a name.

### The lifecycle

Every module goes through the same stages, and every module finishes a stage before any module
starts the next:

1. **Register.** Publish your services, subscribe to events. Do not resolve anything: the modules
   after you have not run yet.
2. **Migrate.** Create the SQL Server tables you own.
3. **Indexes.** Declare the Mongo indexes your collections need.
4. **Start.** Resolve what you need, open what you need, spawn what you need.
5. **Routes.** Hand the mux the endpoints you serve.
6. **Stop.** Release what Start acquired, in reverse order.

That split is the point. Because *everyone* registers before *anyone* starts, mount order does not
decide who can see whom. A module resolving `accountapi.Service` in `Start` finds it whether account
was mounted first or last, and nobody maintains a dependency-ordered list in `main.go`.

Mount order still decides two things, and only two: the order migrations run in, and the reverse
order modules stop in.

### Two ways for modules to talk

**Ask**, when you need an answer — `app.Use[T](k)` resolves an interface from the registry. The type
parameter is the contract, not the implementation, so the providing module can rewrite everything
behind it without a caller noticing.

**Announce**, when you do not — `app.Publish(k, ctx, accountapi.SignedIn{...})`. The publisher does
not know who is listening. Delivery is synchronous, on the publisher's goroutine and inside its
context, so a handler's work is traced and cancelled with the request that caused it. Handlers must
be quick; anything slow spawns its own goroutine, with a context that is not the request's.

Events are facts, so they are published *after* the thing happened. A create that failed publishes
nothing.

**And neither does a delete that deleted nothing** — the harder half, because such a delete usually
*succeeds*. Ending a session that is already gone is idempotent on purpose, and for a while it still
announced a revocation: an administrator passing any session id at all could put "somebody else ended
your session" into an account's feed. The rule that comes out of it is worth stating as a rule, since
the compiler cannot: **if you publish after a write, the write has to tell you whether it did
anything.** That is why `DeleteSession` returns a `bool` it could have discarded.

## Storage: two stores, on purpose

| Store | For | Why |
| --- | --- | --- |
| SQL Server | `account`, `notes`, `authz`, `navigation` | Anything with an invariant. A transaction and a unique constraint are the only things that can say "these two writes must not both succeed." |
| MongoDB | `activity` | Append-only, read newest-first, shape allowed to grow. Nothing here needs a transaction, and forcing it into rows would mean migrating the schema every time an event gains a field. |

The `activity` module is where the second store earns its keep, and it is worth reading for what it
does *not* do: nothing updates an entry, nothing deletes one by hand, no transaction, and no unique
key. Its entire schema management is two indexes. That is the shape of thing Mongo is for, and
anything that starts wanting the missing operations back belongs in SQL Server instead.

**Append-only is not the same as permanent.** Entries are never rewritten; they expire. Retention is
ninety days, and it is a TTL index rather than a sweep this server schedules — a job would be a
second thing to deploy and to keep from running on every replica at once, and Mongo already owns a
monitor for exactly this. The trade is that expiry is approximate to about a minute, which is the
precision "kept for ninety days" deserves. `docs/ACTIVITY.md` has the rest.

Two consequences of no migrations, both of which bite once rather than gradually. A Mongo index
cannot be altered — `EnsureIndexes` creates by name, so a name kept while its keys change fails the
boot of every database that already has the old one, with no forward fix. Name an index after its
keys, so changing them forces a new name. And a TTL is a property of an index rather than of a
collection, so retention arrives through `platform/mongodb`'s `Index.ExpireAfter` — the platform had
to learn the word before the module could use it, which is why what looks like one extra index in a
module was a change to shared code.

**Each module owns a SQL schema named after its module ID** — `notes.Note`, and so on. `Migrate`
creates the schema before a module's first migration runs, so the namespacing is structural rather
than a prefix everyone has to remember. It is also the seam where permissions would go: a module's
credentials can be granted rights on its own schema and nothing else, which turns a review rule into
something the database enforces. On the Mongo side the equivalent is checked in code, because every
collection name passes through `EnsureIndexes`.

SQL style follows [ktaranov/sqlserver-kit](https://github.com/ktaranov/sqlserver-kit): PascalCase
singular objects, no square brackets, no reserved words, `/* */` comments, lower-case data types,
explicit column lists, table aliases, semicolons. The full table of rules is in `CLAUDE.md`. Two
consequences worth stating here, because they shape names rather than formatting:

- A name that needs square brackets to parse is the wrong name. `IDENTITY`, `USER`, `KEY` and
  `SESSION` are reserved, so an identity module's schema cannot be called `identity` and its account
  table cannot be called `User`.
- Because the schema name comes from the module ID, **choosing a module ID is choosing a SQL
  identifier**. Check it against the reserved-word list before you commit to it.

Migrations are owned by the module, keyed by `(module, name)`, and applied in order at boot. Each
commits in its own transaction, so a failure halfway through leaves the earlier ones applied and
recorded, and the next boot resumes where it stopped.

A shipped migration must never be edited. It is keyed by name, so changing its SQL will not re-run
on a database that already has it, and the schema silently diverges. Add another one.

## Why Connect rather than gRPC

The server answers two kinds of caller that have nothing in common.

The client's domain calls want gRPC: a typed stub, binary framing, HTTP/2. The OpenID Connect flow
does not get a choice about its shape — `/authorize` is a URL a browser navigates to, and `/token`
is a form post — and neither do the account endpoints the client's Auth module already calls over
plain JSON.

[connect-go](https://connectrpc.com) is built on `net/http`, so both mount on one `ServeMux`, behind
one port and one certificate, and `net/http` resolves between them by specificity. `grpc-go` would
need a second server or a connection multiplexer in front of both. That Connect also speaks JSON, so
`curl` works on an RPC endpoint, is a convenience rather than the reason.

## The client

The client's own architecture is documented in [`client/docs/architecture.md`](../client/docs/architecture.md).
In outline: a WinUI 3 host composing feature modules, each layered UI → Application → Domain, with
modules reaching each other only through mediator notifications and never through a project
reference.

The seam between the two halves is `IBackendClient` and the module gateways behind it. Everything
above that seam — view models, pages, use cases — is written against interfaces, so which transport
carries a call is a configuration value rather than a design decision.

## Testing

The server's domain, service and platform layers import no I/O. That is not an accident; it is what
makes them testable. Domain tests need nothing at all. Anything that genuinely needs a database is
an integration test and says so.

The client's view models are unit-tested with substituted services; XAML controls are never
constructed, so anything needing the UI thread stays an integration concern.

Neither half's architecture tests may be skipped, disabled or deleted. They are the enforcement
mechanism for everything above.

## Page chrome

Styles live in `Kakehashi.UI.Common/Styles` and are merged once in `App.xaml` through
`ms-appx:///Kakehashi.UI.Common/Styles/…`. They are there rather than in the host because feature
modules have pages too, and a page compiled into a module's own assembly cannot reach a dictionary
only the host has.

Every page with a header uses `controls:PageHeader` — breadcrumb on the left, the page's commands
in one bordered group on the right:

```xml
<controls:PageHeader Section="Administration" Title="Users">
  <StackPanel Orientation="Horizontal" Spacing="2">
    <Button Style="{StaticResource AccentToolbarButtonStyle}" …/>
    <Button Style="{StaticResource ToolbarButtonStyle}" …/>
    <Border Style="{StaticResource CommandBarDividerStyle}" />
    <Button Style="{StaticResource ToolbarButtonStyle}" …/>
  </StackPanel>
</controls:PageHeader>
```

The header is the first thing on every screen and the fastest place for screens to drift apart, so
the layout is one decision in one control and each page supplies only what is its own: what it is
called, and what it can do.

Two rules that came out of building these screens and are worth keeping:

- **A button whose content is an icon plus text has no accessible name.** UIA derives one only from
  simple text content, so every such button needs `AutomationProperties.Name`. Without it the button
  is anonymous to a screen reader and invisible to `winapp ui`.
- **A `ScrollViewer` measures its child at infinite width unless horizontal scrolling is disabled.**
  Leave it on and the widest row decides the width of everything above it, and the card grows past
  the window. `HorizontalScrollMode="Disabled"` and `HorizontalScrollBarVisibility="Disabled"`.

## Navigation groups

The pane is **not** compiled into the client. Which destinations exist, and what protects each one,
is declared in code at `cmd/server/main.go`; where they sit — heading, order, label, offered at all —
lives in the database and an administrator changes it at runtime. The database decides how the app is
arranged; the code decides what it protects.

A client reads its own pane from `NavigationService.GetNavigation`, already filtered for the caller.
`NavigationPlanner` joins that to what the build actually has and to the modules the user attached,
and falls back to the arrangement compiled into the build when the server cannot be reached.

Three outcomes per destination, and the difference between the last two is the point:

| State | Pane |
| --- | --- |
| Permitted | present, reachable |
| Denied, and the destination does not ask to be hidden | **present, disabled** |
| Denied and `HideWhenDenied`, or hidden by an administrator, or detached by the user | absent |

Showing a refused destination rather than hiding it is the same call the server makes when it answers
403 instead of 404: the page is compiled into the client they are running, so hiding buys nothing and
costs the one thing that makes the refusal actionable — being able to tell "not for you" from "not
here", and ask for it. Hiding is reserved for destinations whose existence is itself administrative.
A heading with nothing left under it is dropped rather than drawn empty.

The decision lives in `NavigationPlanner`, not in `ShellPage`: a `Page` cannot be constructed off the
UI thread, so a rule that lives on one is a rule nothing tests.

Full design, schema, reconciliation rules and the two WinUI traps the layout screen paid for:
[docs/NAVIGATION.md](NAVIGATION.md).
