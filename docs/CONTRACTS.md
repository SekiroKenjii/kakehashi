# Contracts

What the two halves promise each other, and how those promises are allowed to change.

## Where the contract lives

```text
proto/kakehashi/<context>/v1/<context>.proto
```

One directory per bounded context, versioned. Both halves generate from it and neither holds a copy:

- **Server** — `buf generate` writes Go and Connect stubs into `server/internal/gen`. The output is
  committed, so a fresh clone builds with no buf installed, and CI regenerates and fails on any
  diff.
- **Client** — `Grpc.Tools` generates the typed client at build time, straight from `proto/`, into
  `client/src/Shared/Kakehashi.Contracts`. There is nothing to commit and nothing to keep in sync.

That one project points at the schema with a relative path:

```xml
<Protobuf Include="..\..\..\..\proto\**\*.proto"
          ProtoRoot="..\..\..\..\proto"
          GrpcServices="Client" />
```

One project rather than one per module, which would have mirrored the server's per-module `rpc/`
package more closely: protoc has to run somewhere, and inside a WinUI project it runs in the
RID-specific inner build the Windows App SDK spawns, writing its output under a different
platform's `obj/` than the compiler reads. A plain library has no inner build. Confinement moves to
`NotesLayeringTests`, which asserts that no Domain or Application layer references
`Kakehashi.Contracts` — the same rule the server states as "only `rpc/` may import `internal/gen`"
and enforces with archlint.

That path is the whole reason this is a monorepo. A pull request that changes a field changes both
halves' generated code in the same CI run, so "the client was built against the old schema" is not a
state this repository can be in.

## Changing the schema

`buf breaking` runs on every pull request with the `WIRE_JSON` rule set. In practice:

| Change | Allowed | Note |
| --- | --- | --- |
| Add a field | yes | New field numbers only. Never reuse a retired one. |
| Add an RPC, add a message | yes | |
| Add a service | yes | |
| Rename a field | **no** | The field number survives, but Connect also speaks JSON, where the name *is* the identifier. `WIRE` alone would allow this; `WIRE_JSON` is why we do not use it. |
| Change a field's type | **no** | |
| Remove or renumber a field | **no** | Use `reserved`. |
| Remove an RPC or a service | **no** | |

When a change really is breaking, add `v2` alongside `v1` and serve both until the old clients are
gone. A desktop client is not a web page: users run the version they installed, for as long as they
like, and an upgrade you cannot force is an upgrade you have to support.

## Ownership

A context in `proto/` belongs to the module of the same name on each side. `kakehashi.notes.v1` is
owned by `server/internal/modules/notes` and `client/src/Modules/Notes`, and nothing else may serve
or extend it.

Contracts do not import each other. Two `.proto` packages that reference each other are the same
mistake as two `api` packages that do — a cycle that has not been noticed yet. Shared scalars belong
in a `kakehashi.common.v1` that depends on nothing.

## What is not in proto, and why

Three parts of the client's traffic are plain HTTP rather than RPC, because their shape is not ours
to choose:

**OpenID Connect.** `/.well-known/openid-configuration`, `/authorize`, `/token`, `/keys`,
`/userinfo`, `/end_session`. These are defined by the OIDC specification, and the client drives them
with a standard library through the system browser. The server implements the spec; there is no
contract of ours to write down.

**Signing in.** Two ways in, and the paths say which is which:

| Path | Who calls it | Shape |
| --- | --- | --- |
| `GET`/`POST /account/browser/sign-in` | the system browser, mid-`/authorize` | HTML form |
| `POST /account/sign-in` | the desktop client, directly | JSON, returns a token response |
| `POST /account/sign-out` | the desktop client, directly | JSON |

`/account/browser/sign-in` is not something the client calls. It is where `/authorize` redirects an
unauthenticated browser, and its response is a page for a human. `/account/sign-in` is the first-party
path: the client collects the password itself and gets tokens back in one round trip, no browser.

Both end at the same place — a session row and a token minted by the same provider — so refresh and
revocation stay on the standard OAuth endpoints regardless of which door was used. That is the whole
reason the in-app path mints through `op` rather than signing its own JWTs.

**The account endpoints.** `GET`/`PUT /account/profile`, `POST /account/password`,
`GET`/`DELETE /account/sessions`, `POST /account/sessions/revoke-all`,
`GET /account/security-events`. These are the shapes the client's existing Auth module already
calls. They are pinned by the client, not by us, which makes them a contract with no linter behind
it — so they are documented here and covered by integration tests instead.

Everything else — every domain call the application makes — goes through `proto/`, where the build
can see it.

## Transport

The server speaks three protocols on one port, because Connect does:

| Protocol | Used by | Content-Type |
| --- | --- | --- |
| gRPC | the WinUI client's generated stubs | `application/grpc` |
| Connect (JSON) | `curl`, debugging, anything without a stub | `application/json` |
| gRPC-Web | a browser client, if one ever exists | `application/grpc-web` |

Which one the client uses is `Backend:Protocol` in `appsettings.json`. It defaults to gRPC; the JSON
path exists so that a failing call can be reproduced from a terminal without writing a program:

```sh
curl -X POST http://localhost:8080/kakehashi.health.v1.HealthService/Ping \
  -H 'Content-Type: application/json' \
  -d '{"message":"hello"}'
```

## Errors

Handlers return plain Go errors. An interceptor in `server/internal/platform/rpc` maps
`errs.Kind` onto a Connect code and replaces the message with one that is safe to send:

| `errs.Kind` | Connect code |
| --- | --- |
| `NotFound` | `not_found` |
| `Invalid` | `invalid_argument` |
| `Conflict` | `already_exists` |
| `Unauthenticated` | `unauthenticated` |
| `Forbidden` | `permission_denied` |
| `Internal` (and anything unclassified) | `internal` |

`Internal` is the only kind that is logged, and the only kind whose message never crosses the wire.
Its text is written for whoever reads the log and tends to carry connection strings and SQL; on a
server reachable from the internet, the difference between logging that and returning it is the
difference between a diagnostic and a disclosure.
