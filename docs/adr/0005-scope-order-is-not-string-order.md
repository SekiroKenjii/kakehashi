# 0005. Row scopes rank by explicit order, not string sort

Date: 2026-08-12
Status: accepted

## Context

The three row scopes rank `own` < `team` < `all`, but the names sort alphabetically as
`all` < `own` < `team` — the alphabetically largest is the narrowest. `GrantsOfAccount`, the one
query on the request hot path, used to widen a caller's scopes across roles with `MAX(rp.Scope)`
over the nvarchar column, under a comment claiming the names sort the way they rank. Nothing tested
that claim: the test that appeared to was asserting `auth.Widest`, which ranks correctly in Go.
An account holding one role at `all` and another at `team` therefore resolved to `team` — the
narrower of the two. This was a real security defect that cost a live debugging session.

## Decision

`GrantsOfAccount` (server/internal/modules/authz/store/role.go) folds on an explicit rank in SQL:
`MAX(CASE rp.Scope WHEN N'all' THEN 3 WHEN N'team' THEN 2 WHEN N'own' THEN 1 ELSE 0 END)` grouped
by permission key. The widening stays in SQL — one join, one row per permission — because
pulling every grant back to merge in Go would move the same data and then do the work twice.

## Consequences

- Scope strings must never be compared or aggregated directly; widening goes through the SQL
  `CASE` rank or `auth.Widest` in Go. An unrecognised scope ranks 0, below `own` — it narrows.
- `server/internal/platform/auth/scope_order_test.go` exists solely to keep this shut: it fails if
  someone reinstates `MAX` reasoning by proving the two orders differ, and it fails deliberately
  if a rename ever makes them coincide, forcing a review of everything relying on the difference.
- Adding or renaming a scope requires updating the `CASE` ranks and re-checking that guard test.
