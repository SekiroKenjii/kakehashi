# 0026 — Plugin artifacts stream over the contract, not from a static route

## Context

A catalog of plugins has to hand out bytes. Every other way into this server is a Connect RPC on one
`net/http` mux, under the route-policy machinery the kernel refuses to boot without: a route states
`Public()`, `SignedIn()`, `ModuleAccess()` or `Permission(key)` beside its pattern, or it is not
collected.

Serving a package from a static route would be the first file-serving in the codebase. It would also
be a second way in, with its own answer to who may ask, outside `buf`'s coverage and outside the
interceptor that maps a service's refusals to status codes.

A package is also far larger than a message is meant to be.

## Decision

**Artifacts move over a server-streaming RPC**, `PluginService.DownloadPluginVersion`, in chunks.
Content lives in `varbinary(max)` and is read out with `SUBSTRING`, so nothing between the database
and the socket holds a whole package. A publish-time size cap — 64 MB — is enforced where the bytes
arrive.

The client hashes as it writes and compares against the digest the catalog published, before
anything opens the archive. **The digest is the server's own**: it hashes what arrived at publish
and refuses a mismatch, so the value a client compares against is one this server produced rather
than one an uploader asserted.

Two routes, because they are two policies: `ModuleAccess()` to read the catalog and download,
`Permission(plugins.manage)` to change what is on offer. `unprotectedRouteModules` is unchanged —
`plugins` declares nothing public.

## Consequences

Everything about a package travels the path every other request travels, and is described by the
same contract `buf breaking` guards. There is still no static route in this server.

`varbinary(max)` is the smaller first step, not the end state. A `platform/blobstore` package with a
filesystem root is the alternative and is the better one if artifacts grow: it changes the store and
nothing above it, because the streaming RPC is unchanged either way. If it lands it takes an
archlint rule of its own — only the `plugins` module may import it — in the shape rule 5 already
uses for `platform/database`.

Yanking a version hides it and never deletes it, so an account that already installed one can still
repair its installation. A deleted version would make an installed one unexplainable.
