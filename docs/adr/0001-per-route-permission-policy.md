# 0001. Every route declares its own access policy

Date: 2026-08-12
Status: accepted

## Context

Access control used to be a list of modules exempt from the permission gate, so exempting a module
exempted every route it served: all thirteen of the account module's routes skipped the check, and
its administrative user directory was protected only by a hand-written `auth.RequirePermission`
wrapper around that one handler, in each module.go. Deleting that call — or adding a second admin
route and forgetting to wrap it — opened the directory to any signed-in caller, and nothing caught
it. An earlier gate also passed anonymous requests through, so omitting the token skipped the check.

## Decision

Every `app.Route` states a `RoutePolicy` beside its pattern, and the kernel refuses at boot to
collect a route whose policy is the zero value. The only non-zero values come from four
constructors — `Public()`, `SignedIn()`, `ModuleAccess()` (the module's `<id>.access`), and
`Permission(key)` — and the mux applies each policy as one wrapper per route. The composition root
(`unprotectedRouteModules` in server/cmd/server/main.go) names the only modules allowed to declare
Public or SignedIn; boot refuses those from anyone else. Four qualify, each because the server
breaks without it:

| Module | Why it may declare an unprotected route |
| --- | --- |
| `health` | a liveness probe that needs an account is not a liveness probe |
| `account` | signing in cannot require a permission only obtainable after signing in, and OpenID Connect must answer an anonymous browser |
| `authz` | a module that answers "what may I do" cannot require permission to answer |
| `navigation` | a client cannot draw a locked door before it knows the door is there, so an account with no grants must still be able to ask what its pane holds |

All four also serve administrative surfaces, and each of those names its own permission on its own
route. Being on the list buys permission to *ask* for an unprotected route, never blanket exemption.

## Consequences

- A forgotten policy is a failed boot, not an open endpoint; enforcement lives in the mux, so no
  hand-written wrapper exists whose deletion silently disables the check.
- Anonymous callers to permission-gated routes get 401 — omitting a token grants nothing — and
  missing grants get 403 naming the permission, because the endpoint is compiled into the client.
- A policy covers everything its mounted pattern reaches, including whole routers: the OIDC
  provider at "/" and each Connect service (one route, N procedures). A finer check inside a
  handler adds to the policy; it never substitutes for it.
- Grants resolve once per authenticated request on every route (a primary-key lookup), because the
  exempt modules also serve admin surfaces; an unreachable policy store fails closed with 503.
- The root's list grows per security exemption, never per added module — a module able to exempt
  itself would do so in one line of its own file, and modules are added by copying one.
