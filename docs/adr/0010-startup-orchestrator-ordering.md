# 0010. Startup is ordered orchestrators, and the order is load-bearing

Date: 2026-08-12
Status: accepted

## Context

App startup had steps that each depend on an earlier one: the permission and navigation-layout
fetches need the token the authentication gates produce, the shell builds the navigation pane from
those two answers, the theme service needs main-window content to exist, and the splash must stay
up until the main window is ready. Running the permission fetch after the shell would build the
navigation pane once wrong and then correct it in front of the user; splitting the permission and
layout fetches into two ordered steps was rejected because a pane drawn from only one of them is a
pane drawn wrong.

## Decision

Startup is a list of `IStartupOrchestrator` singletons; `AppOrchestrator` sorts them by integer
`Order` and awaits each in turn: Splash 10, Authentication 15, Permission 17, Shell 20, Theme 30,
Activation 40. `AuthenticationOrchestrator` runs every registered `IAuthenticationGate` and is a
no-op when none is registered, which is what keeps the Auth module optional.
`PermissionOrchestrator` fetches permissions and navigation layout together at 17 — after 15
because both calls need a token, before 20 because the shell needs both answers — and re-fetches
both on every `AuthSessionChangedMessage`.

## Consequences

New steps slot in by number, and the numbers carry invariants: anything needing a token goes after
15, anything the navigation pane depends on goes before 20, anything touching main-window content
goes after 20, and Activation stays last so the splash covers everything. The session-change
re-read means signing in as somebody else replaces the predecessor's permissions instead of
inheriting them — a security property on shared machines. Inside that re-read, permissions must
refresh before layout: the layout's Changed event rebuilds the pane, so the reverse order rebuilds
once against the previous account's permissions. The messenger registration happens after the
first fetch, not in the constructor, so the sign-in performed by startup itself does not trigger a
second, redundant call.
