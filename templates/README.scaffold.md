# __APP_TITLE__

**A Windows desktop app and the server behind it, in one repository.**

A WinUI 3 client, a Go backend that deploys to any Linux box, and one contract the build checks
against both halves — so the two cannot drift apart without something going red.

---

## The first five minutes

Prerequisites: Go 1.26+, [buf](https://buf.build/docs/installation), Docker, and the .NET 10 SDK.
Building or running the client additionally needs Windows and the Windows App Runtime.

**1. Start the backend.** SQL Server, MongoDB and the server itself are one compose file.

```sh
docker compose up -d
curl http://localhost:8080/healthz   # 200
```

**2. Run the client.**

```pwsh
dotnet run --project client/src/App/__APP_NAME__.App/__APP_NAME__.App.csproj -p:Platform=x64
```

**3. Watch the Home page tick.** The Backend card reads **Connected**, and the Getting started
checklist ticks itself as you go: the backend when it answers, the example module when it has
something in it. Everything else on that card is a command with a copy button — including the two
below.

If the Backend card reads **Offline**, the card carries the command that fixes it and a Retry
button beside it. Nothing else needs to be true for the client to start.

## Add your first module

```sh
kakehashi add module orders
```

That writes both halves — `proto/__PROTO_PACKAGE__/orders/v1/`, the server module under
`server/internal/modules/orders/`, the client Domain/Application/UI projects, one CRUD page — and
every line of wiring that mounts them. All three gates stay green with no hand edits; that is the
point of the command, and CI checks it on every push.

`kakehashi add page orders Archive` adds a page to a module that already exists.

Full reference: [`docs/cli.md`](docs/cli.md). A worked example, from the command to a working
feature: [`docs/first-module.md`](docs/first-module.md).

## The three gates

A modular monolith only stays modular if something checks, and here the check has to hold across a
network as well as inside a process.

| Gate | What it protects | Command |
| --- | --- | --- |
| `archlint` | module boundaries **inside** the Go server | `cd server && go run ./tools/archlint` |
| `__APP_NAME__.ArchitectureTests` | the three layers **inside** the WinUI client | `cd client && dotnet test __APP_NAME__.slnx` |
| `buf breaking` | the contract **between** them | `buf breaking --against '.git#branch=main'` |

Inside the server, a module is reachable only through its `api` package. Inside the client, a module
is reachable only through mediator notifications. Between the two, the only thing either side may
assume about the other is what is written in `proto/`.

None of the three is optional, and all three run on every push. What each one refuses, and how to
read what it prints: [`docs/gates.md`](docs/gates.md).

## Remove the example

The Notes module is one feature end to end — proto contract, `api/domain/store/service/rpc` on the
server, Domain/Application/UI on the client, one page, its tests. It is there to be read and then
deleted:

```sh
kakehashi remove module notes
```

It leaves nothing behind: both module trees, the generated code, the proto directory, the test
projects and every line of wiring go together. Removing it leaves the frame with no feature module,
which is exactly what `kakehashi new --bare` produces. Step by step:
[`docs/remove-example.md`](docs/remove-example.md).

## Layout

```text
proto/          the contract. One directory per bounded context, versioned.
server/         Go modular monolith. Compiles to one static binary.
client/         WinUI 3 modular monolith. Ships as an .exe or an MSIX.
docs/           why the pieces are shaped the way they are
```

The server:

```text
server/
  cmd/server/            the composition root: the only file that knows every module
  internal/
    app/                 the kernel: the Module contract, the service registry, the HTTP mux
    platform/            what every module may use: config, logging, SQL Server, Mongo, event bus
    modules/<id>/
      api/               the contract: interfaces, DTOs, events. The only importable package.
      domain/            entities and the rules they enforce. No SQL, no protobuf.
      store/             persistence. Its tables live in the module's own SQL schema,
                         `<id>.Thing`; its Mongo collections are prefixed `<id>_`.
      service/           use cases. Orchestrates domain and store; publishes events.
      rpc/               the wire: maps api types to and from generated protobuf.
      module.go          the wiring.
  tools/archlint/        the boundary checker
```

`rpc/` is the one addition. It exists for the same reason `api/` does: generated protobuf types are
someone else's shape, and letting them into `domain/` or `service/` means a change to the wire format
becomes a change to your business rules. `rpc/` is the only package allowed to import the generated
code, and `archlint` enforces it.

## The stack

| Piece | Choice |
| --- | --- |
| Client | WinUI 3 / Windows App SDK 2.1, .NET 10, `CommunityToolkit.Mvvm` |
| Server | Go 1.26 |
| Transport | [connect-go](https://connectrpc.com) — serves gRPC, gRPC-Web and JSON on one port |
| Contract | Protocol Buffers, linted and breaking-checked by [buf](https://buf.build) |
| Storage | SQL Server for anything transactional; MongoDB for append-only feeds |
| Identity | The server is its own OpenID Connect provider ([zitadel/oidc](https://github.com/zitadel/oidc)); the client signs in in-app by default, or through the system browser with Authorization Code + PKCE |
| Observability | OpenTelemetry on both sides, OTLP out |

**Why in-app sign-in is the default.** Handing the user to the system browser is right when the
identity provider belongs to someone else — Entra, Okta, Google — because then the point is that the
password is typed into *their* page and this application never sees it, and because SSO, MFA and
conditional access all live there. None of that holds when the provider is the same process the
client already talks to: the password crosses the same trust boundary either way, and the user pays
a window that steals focus and a loopback listener corporate firewalls dislike. So `Auth:Mode`
defaults to `InApp`, and flipping it to `Browser` is one line in `appsettings.json` — which is the
line to change the day `Auth:Authority` stops pointing at this server.

**Why connect-go rather than plain gRPC.** The server has to answer two different kinds of caller.
The client's domain calls want gRPC. The OpenID Connect flow does not get a choice: `/authorize` is a
URL a browser navigates to, and `/token` is a form post. connect-go is built on `net/http`, so both
mount on one `ServeMux`, behind one port and one certificate. Plain gRPC would need a second server
or a connection multiplexer. That it also speaks JSON, so `curl` works, is a bonus rather than the
reason.

## Testing

```pwsh
cd client && dotnet test __APP_NAME__.slnx
```

```sh
cd server && go test ./...
```

To include the client tests that talk to a running backend rather than skipping them:

```pwsh
$env:__APP_NAME_UPPER___TEST_BACKEND = "http://localhost:8080"; dotnet test client/__APP_NAME__.slnx
```

## Deployment

The server compiles to one static binary with no runtime dependencies:

```sh
cd server && CGO_ENABLED=0 GOOS=linux go build -o __APP_NAME_LOWER__ ./cmd/server
```

Copy it to a Linux host, point it at your SQL Server and MongoDB, and run it behind a reverse proxy
that terminates TLS. TLS is not optional in production: the OpenID Connect endpoints handle
credentials, and browsers will refuse to send them over plain HTTP anyway.

## Where to look first

- [`docs/getting-started.md`](docs/getting-started.md) — the five minutes above, at walking pace
- [`docs/first-module.md`](docs/first-module.md) — `add module`, then making it do something
- [`docs/gates.md`](docs/gates.md) — the three gates, and how to read what each one prints
- [`docs/cli.md`](docs/cli.md) — every command and flag
- [`docs/faq.md`](docs/faq.md) — Windows only? A different database? Where does it deploy?
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the reasoning behind the shapes
- [`docs/CONTRACTS.md`](docs/CONTRACTS.md) — what the two halves promise each other, and how those
  promises are allowed to change
- [`docs/RBAC.md`](docs/RBAC.md) — who may do what, to which rows, and where the scope is honoured
- [`docs/NAVIGATION.md`](docs/NAVIGATION.md) — how the pane is arranged, and who decides what
- [`CLAUDE.md`](CLAUDE.md) — the same rules, written for an AI agent working in this repository
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — the branching model, and how a release is cut
