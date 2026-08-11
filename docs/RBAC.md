# Authorization: RBAC with row scopes

## What was wrong

`account.Account` carried `Roles nvarchar(400)` — a space-joined string. That is a claims shortcut,
not RBAC: there is no role entity, no permission catalogue, nothing a screen can render or an
administrator can change. It was defensible when exactly one role existed and was checked in exactly
one place. It stops being defensible the moment roles, permissions and row scopes are the
requirement.

The `assignments` module made the same mistake one level up: it modelled "which modules may this
account use" as its own bespoke ACL, when module access is simply a permission like any other.

## The model

One bounded context — authorization — in the `authz` module, owning the `authz` SQL schema.

```text
Account ──< AccountRole >── Role ──< RolePermission >── Permission
                                          │
                                        Scope          ← row-level security
```

| Table | Holds |
| --- | --- |
| `authz.Permission` | the catalogue: key, name, description, category, high-risk flag |
| `authz.Role` | a named set of grants: id, name, description, system flag |
| `authz.RolePermission` | the grant, **and its scope** |
| `authz.AccountRole` | who holds which role |
| `authz.AuditEntry` | every grant, revoke and scope change, with who and when |

`Permission` is seeded from a catalogue the modules declare, not typed into a table by hand: a
permission that no module claims is a permission nothing enforces.

## Row-level security: the scope rides on the grant

Every `RolePermission` carries a scope. It is not a second system bolted beside RBAC — it is a
column on the grant, which is what keeps "who may do this" and "to which rows" in one place a
reviewer reads at once.

| Scope | Means |
| --- | --- |
| `all` | every row |
| `team` | rows owned by an account sharing the caller's `TeamId` |
| `own` | rows the caller owns |

**A user's effective scope is the widest their roles give them** — `all` beats `team` beats `own`.
Two roles cannot combine to *narrow* access; that would make adding a role a way to lose access,
which is the surprise every permission system regrets.

`team` needs a notion of team, so `account.Account` gains a nullable `TeamId`. That is the seam a
product redefines — department, tenant, region — and it is deliberately one nullable column rather
than a hierarchy nobody asked for.

### Where the scope is actually honoured

A scope only means something where a store narrows its own query on it, so a permission declares
whether it does. `authzapi.Permission.IsScoped` is that declaration, it reaches the client on the
wire, and the administration screen offers the own/team/all picker **only** for permissions that
carry it. Everywhere else the control is absent rather than inert — an earlier version showed the
picker for every permission, stored the choice, displayed it back, and changed no answer anywhere.

In this build exactly one permission declares it: `users.manage`. `account`'s store narrows
`Accounts` on `auth.ScopeOf` — `own` returns the caller alone, `team` matches on `TeamId` (a null
team matches nobody, including other nulls), `all` returns everything, and a scope it does not
recognise returns nothing rather than everything. The route gate has already established that the
caller holds the permission; if the store cannot tell how far it reaches, the safe answer is the
smaller one.

The narrowing lives in the store rather than the gate: a gate that rewrote everyone's SQL would
have to understand everyone's schema, while a store narrowing its own query only has to understand
its own.

**One trap, written down because it cost a live debugging session.** The three scope names do not
sort the way they rank — alphabetically `all` < `own` < `team`, so `MAX(Scope)` over the column
picks the *narrowest*. The fold is done on an explicit `CASE` rank instead, and
`platform/auth/scope_order_test.go` exists solely to fail if somebody puts `MAX` back. The defect
and the decision are recorded in [ADR 0005](adr/0005-scope-order-is-not-string-order.md).

## Enforcement

The kernel stamps `Route.Module`, and every route declares its own `app.RoutePolicy` — `Public`,
`SignedIn`, `ModuleAccess` or `Permission(key)` — which the mux enforces. Boot refuses a route that
declares nothing, and refuses one that checks no permission unless the composition root named its
module in `unprotectedRouteModules`. What the check asks is:

```go
type Permissions interface {
    Resolve(ctx context.Context, subject Subject) (Grants, error)
}

type Grants map[string]Scope
```

The route gate requires `<module>.access`. Everything finer — `users.manage`, `devops.sql` — is
asked for by the handler that needs it, through the same resolved `Grants` the gate already put on
the context, so it costs no extra query.

**Permissions are resolved per request, never from the token.** An access token lives ten minutes;
a permission revoked five minutes ago must not keep working for five more. The token still carries a
`roles` claim, but nothing authorizes on it — it is there for display and OIDC conformance.

## Row scope in practice

A service that reads rows asks for its own scope and narrows:

```go
scope := auth.ScopeOf(ctx, "notes.read")
switch scope {
case auth.ScopeAll:  // no filter
case auth.ScopeTeam: // WHERE n.OwnerTeamId = @caller.TeamId
case auth.ScopeOwn:  // WHERE n.OwnerId = @caller.ID
}
```

The filter is the store's, not the gate's. A gate that tried to rewrite everyone's SQL would have to
understand everyone's schema; a store that narrows its own query only has to understand its own.

## What the screens need

**Users** — the account list with role chips, status, last login; a detail panel with sessions and
the danger zone. Roles are many-to-many, so the column shows the first chip and `+N`.

**Role Permissions** — roles down the left with permission and user counts; the catalogue grouped by
category with per-group *All on* / *All off*; a scope selector beside each enabled grant; staged
edits with an unsaved-changes bar, Save and Discard; an audit log.

Staging is client-side on purpose. An administrator flipping eight toggles is composing one
decision, and a system that applies each flip as it happens gives them no way to change their mind
and gives the audit log eight entries for one act.

## What shipped

The contract is `proto/kakehashi/authz/v1/authz.proto` plus `proto/kakehashi/account/v1/account.proto`,
and the split between them is the module boundary: the account module owns people, the authorization
module owns what they may do. Nothing carries a copy of the other's fact, so the two cannot
disagree — the users screen calls both and joins by id.

Three services, three different guards:

| Service | Guard | Why |
| --- | --- | --- |
| `AuthzService` | signed in | A module that answers "what may I do" cannot require permission to answer. |
| `AuthzAdminService` | `roles.manage` | Whole route, so every procedure added later inherits it. |
| `AccountAdminService` | `users.manage` | Same. |

The guard is `auth.RequirePermission` wrapped around the route in the module's `Routes`, never a
check inside a handler. A handler that forgets the check is a handler nothing catches, and the one
somebody forgets is the breach. The single exception is `ListAuditEntries`, which needs `audit.view`
on top of the `roles.manage` its route already required — three lines in the handler beat a second
Connect service for one procedure.

Two things the screens needed and the schema did not have:

- `account.Account.LastSignInAt` (nullable) and `IsActive` — migration `0003_account_status`. Three
  of the four stat cards depend on them. `IsActive` is enforced at sign-in and revokes every session
  on the way down, because a deactivation that leaves live tokens working for another ten minutes is
  a deactivation in name only.
- `authz.AuditEntry.ActorName` and `RoleName` — migration `0002_audit_names`. Denormalised on
  purpose: a trail read months later, after the administrator is deleted and the role renamed, must
  not render a blank exactly where the reader is looking.

On the client, `ModuleAssignmentService` is gone and `PermissionService` replaces it. Module access
is now the ordinary permission `<module>.access`, so the lock a page draws and the refusal a route
returns read the same row. The shell's administration items are revealed by permission rather than
by the literal role name `admin`, which the client and server previously agreed on only by luck.

One behaviour deliberately changed with it: holding `<module>.access` no longer forces a module into
the user's composition. A grant means they *may* use it; whether they *do* stays their preference.
`IModuleRegistry.IsGranted` is therefore always false today — kept rather than removed, because the
concept ("an administrator pushed this at you") may come back and the interface is the place to
decide that, not a caller.

## The lockout you can reach in two clicks

An administrator could switch `roles.manage` off on the role they themselves held, press Save, and
be refused by their very next request — including every request the screen that did it makes. There
is no way back through the product: putting it back needs the permission that was just removed. The
only recovery is `KAKEHASHI_AUTHZ_BOOTSTRAP_ADMIN`, i.e. a redeploy.

`Service.ensureActorKeepsControl` now refuses it, and refuses the two other routes to the same
place: deleting a role that is the actor's only source of `roles.manage`, and unassigning it from
themselves. One SQL question answers all three — *would this account still hold `roles.manage` if
that one role stopped granting it?*

Three things about where the check lives:

- **On the server, not the client.** A client check is a hidden button; this is a rule.
- **Only for your own access.** Another administrator demoting you is ordinary and leaves somebody
  able to put it back. The guard fires only when the actor and the target are the same account.
- **Not in the domain.** `Role.Revoke` stays neutral, because whether a revocation is safe depends
  on who is asking — which the aggregate has no business knowing.

The refusal names the reason: *"This would remove your own roles.manage permission, and nothing in
the app could give it back. Ask another administrator to make this change."*

One thing the client had to fix to show it. The route gate answers with a plain HTTP 403 — that
middleware runs before Connect sees the request — so the sentence the server wrote never crosses
the wire, and the client rendered gRPC's own *"Bad gRPC response. HTTP status code: 403"*. Refusals
that arrive without words now get a sentence that at least names the next step, and a refused call
re-reads the caller's grants so the screen redraws as locked rather than as a working screen that
answers 403 to everything.
