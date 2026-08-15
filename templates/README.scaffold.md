# __APP_TITLE__

**A Windows desktop app and the server behind it, in one repository.**

A WinUI 3 client, a Go backend that deploys to any Linux box, and one contract the build checks
against both halves — so the two cannot drift apart without something going red.

---

Both halves are modular monoliths. Both halves have a linter that fails the build when a module
reaches past a boundary it was not given. The third linter guards the boundary between them.

## The three gates

A modular monolith only stays modular if something checks, and here the check has to hold across a
network as well as inside a process.

| Gate | What it protects | Command |
| --- | --- | --- |
| `archlint` | module boundaries **inside** the Go server | `cd server && go run ./tools/archlint` |
| `__APP_NAME__.ArchitectureTests` | the three layers **inside** the WinUI client | `cd client && dotnet test` |
| `buf breaking` | the contract **between** them | `buf breaking --against '.git#branch=main'` |

Inside the server, a module is reachable only through its `api` package. Inside the client, a module
is reachable only through mediator notifications. Between the two, the only thing either side may
assume about the other is what is written in `proto/`.

None of the three is optional, and all three run on every push.

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

## Getting started

Prerequisites: Go 1.26+, [buf](https://buf.build/docs/installation), Docker, and the .NET 10 SDK.
Building or running the client additionally needs Windows and the Windows App Runtime.

```sh
docker compose up -d          # SQL Server, MongoDB, and the server
curl localhost:8080/healthz   # 200
```

```pwsh
dotnet run --project client/src/App/__APP_NAME__.App/__APP_NAME__.App.csproj -p:Platform=x64
```

The home page's Backend card should read **Connected**, and the Notes page should let you write
one, edit it and delete it — through gRPC, into SQL Server.

To include the tests that talk to a running backend rather than skipping them:

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

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the reasoning behind the shapes
- [`docs/CONTRACTS.md`](docs/CONTRACTS.md) — what the two halves promise each other, and how those
  promises are allowed to change
- [`docs/RBAC.md`](docs/RBAC.md) — who may do what, to which rows, and where the scope is honoured
- [`docs/NAVIGATION.md`](docs/NAVIGATION.md) — how the pane is arranged, and who decides what
- [`docs/ACTIVITY.md`](docs/ACTIVITY.md) — what the feed records, the one write that comes from
  outside, and how long it keeps things
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — the branching model, and how a release is cut
