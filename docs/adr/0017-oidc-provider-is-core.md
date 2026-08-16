# 0017. The OpenID Connect provider is CORE; its seed values are configuration

Date: 2026-08-15
Status: accepted

## Context
The `account` module makes the server its own OpenID Connect provider, and the `Auth` client module
signs in through it either in-app or through the browser
([0007](0007-in-app-sign-in-alongside-browser-oidc.md)). That is a large amount of code to hand
someone who wanted a desktop app skeleton, and the obvious alternative is to make it optional and
let a project bring its own identity provider. `docs/pivot/01-PHASE-0-INVENTORY.md` D2 recommends
keeping it CORE and treating the seeded user and role as EXAMPLE.

## Decision
Both halves of the authentication path are CORE: `server/internal/modules/account`,
`server/internal/modules/authz`, and `client/src/Modules/Auth`. A desktop client talking to a
server needs tokens on the first day, and a template that stops at "wire up your own IdP" has
handed back the hardest part of the wiring. It is also what makes `app.Permission(key)` mean
anything — take the provider out and every route policy becomes decoration.

The seeded developer account and the bootstrap administrator are not code and not a removable unit.
They are four environment variables in `docker-compose.yml` — `KAKEHASHI_ACCOUNT_SEED_EMAIL`,
`…_PASSWORD`, `…_NAME` and `KAKEHASHI_AUTHZ_BOOTSTRAP_ADMIN` — read at boot and idempotent after.
Phase 1 turns their values into placeholders derived from the scaffold inputs; the mechanism that
reads them stays.

## Consequences
Every scaffolded project starts with a working sign-in and a first administrator, which is what
makes the five-minute path in the README real. It also means every scaffolded project carries an
OIDC provider it may not want, and `--auth none` is deferred rather than free: removing it means
unpicking `authz`, the route policies and the client's authentication gate, so it is a v1.1 unit
with its own dependency pass, not a flag added late. `archlint` rule 7 — only `account` may import
an OIDC or JWT library — is unaffected and still the thing that keeps the provider in one module.
