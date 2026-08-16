# 0018. Database-driven navigation stays CORE; only its editor leaves

Date: 2026-08-15
Status: accepted, and it departs from the recommendation

## Context
`docs/pivot/01-PHASE-0-INVENTORY.md` D3 recommends static navigation as the CORE default, with the
descriptor-and-reconcile machinery moved to `showcase` and a `--nav db` flag considered for v2.
Phase 0 read the import graph before writing the map, and the recommendation does not survive it:
`navigation/api` declares the `Contributor` interface that `account`, `authz`, `activity` and
`notes` each implement in their own `navigation.go`. Two of those four are CORE. The client has no
static path to fall back to either — `NavigationService`, `NavigationPlanner` and the shell all
read what the server serves.

## Decision
The `navigation` module stays CORE, whole. What leaves under D1 is the *editor*: the
`NavigationLayoutPage` screens and the admin service behind them, which is what an application
author replaces anyway. A scaffolded project therefore keeps: modules declaring their own
destinations, the server reconciling them into rows, and the client rendering what it is served.

Static navigation, if it is wanted, is an addition in v2 — `--nav static` writing a client-side
registry — not a subtraction now. Framing it as a subtraction was the error: deleting the module
does not yield a simpler default, it yields a client with no pane and a compile error in two CORE
modules.

## Consequences
The recommendation is recorded as not taken, so the next reader does not re-derive the import graph
to find out why. A scaffolded project carries a `navigation` SQL schema it did not ask for, which
is the cost, and the benefit is that `kakehashi add module orders` puts an Orders entry in the pane
without touching a registry — the generator writes one `navigation.go` in the module it is already
writing. If a later phase does want the static default, it is a new record superseding this one,
and it owes a client-side registry before it owes a deletion.
