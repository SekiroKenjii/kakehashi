# 0015. Attach/detach is composition preference; the server enforces access

Date: 2026-08-12
Status: accepted

## Context
Every module stays compiled in with its services registered; attaching and detaching only change
what the shell and pages present. The server also assigns modules per account, so two unrelated
reasons a module can be unavailable met in one place: detaching is the user's own reversible
preference, withholding is an administrator's decision the user cannot overrule. The navigation
planner once asked "attached?" only when a module was not withheld, so a module the user had
detached reappeared, disabled, the moment an administrator withheld it.

## Decision
`IModuleRegistry` keeps the two states separate. Attachment is keyed by the client module name;
withholding and grants by the server's module id (they differ: `Auth` vs `account`), so each
question is asked with its own key. The planner asks "detached?" first, independently of
withholding: a detached module is skipped outright, a withheld one is listed but disabled; a
withheld module can never be attached, a granted one never detached. None of this is a security
boundary: the server refuses an unassigned module's requests at one place that sees every request.

## Consequences
Client-side gating is presentation only — a lock drawn instead of a button that would fail — so
a stale or tampered client leaks no access; the server refusal is the enforcement. Before
`SetAssignments` runs (once, after sign-in) both sets are empty, which reproduces a build without
assignments: an assignment fetch that never returns leaves the app as it was rather than empty,
and the server still refuses. Future changes must keep preference and permission as separate
questions asked in that order with their own identifiers; folding one into the other re-creates
the detached-module-reappears defect, and nothing stored client-side may be read as authorization.
