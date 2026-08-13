# 0009. gRPC clients are registered in one place with a resolve-time address

Date: 2026-08-12
Status: accepted

## Context
Every gRPC call to the backend must carry the session bearer token. A module that assembled its
own channel and forgot the call credentials would send unauthenticated requests, and the defect
would stay invisible until the server started checking tokens. Registration order was a second
hazard: an address captured at registration time would be stale or missing if a module registered
its client before `AddBackendInfrastructure` had bound `BackendOptions`.

## Decision
`AddBackendGrpcClient<TClient>` in `Kakehashi.App.Infrastructure` is the single way to register a
generated gRPC client; feature modules call it from `IModule.RegisterServices` instead of building
a channel themselves. It reads the backend address from `IOptions<BackendOptions>` at resolve time,
attaches the `IAccessTokenProvider` token to every call via call credentials, and sets
`UnsafeUseInsecureChannelCallCredentials = true` on the channel.

## Consequences
- Token attachment cannot be half-done: every client registered through the helper carries the
  bearer token, and there is no supported path that skips it.
- Modules can register before or after `AddBackendInfrastructure`; the resolve-time address read
  makes the order irrelevant.
- The insecure-channel override is load-bearing: call credentials normally require a secured
  channel, and the development backend is reached over plain HTTP, so without it no token is ever
  sent. In production TLS is terminated by the reverse proxy in front of the backend.
- The default `NullAccessTokenProvider` yields no token, so the Authorization header is omitted
  and an unauthenticated backend still works; the Auth module swaps in a session-backed provider.
