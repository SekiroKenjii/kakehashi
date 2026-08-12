# 0004. Navigation and role edits are staged on the client and applied atomically

Date: 2026-08-12
Status: accepted

## Context
The navigation admin surface began as write-per-edit: each control wrote the moment it changed,
through single-row procedures (CreateGroup, UpdateGroup, DeleteGroup, MoveItem, UpdateItem). That
was sound while one call was one change and a transaction had nothing to protect. The gesture
changed it: dragging a screen into another heading renumbers what it lands among, so one gesture
became several writes, and a sequence of single-row calls can fail halfway. The defect that forced
the issue was exactly that — a reorder was two MoveItem calls, the second failed, and both rows
were left sharing a sort number. On the role screen, saving per toggle also gave an administrator
no way to change their mind and wrote one audit entry per click for what was one decision.

## Decision
Both admin screens stage edits in the view model and apply once. `ApplyLayout` takes the whole
desired arrangement, validates everything before writing anything, and writes all of it or none.
`SaveGrants` replaces a role's entire grant set, delete-then-insert rather than a diff, in one
transaction — what the screen sends IS the whole set. Discard rebuilds the tree from the last-read
snapshot rather than asking nodes to undo themselves: which heading a node sat under, and in what
order, are facts about the tree that no single node knows.

## Consequences
A refusal leaves the stored state exactly as it was: no half-rearranged pane, no silently
half-applied edit batch, and one audit entry per decision. Unsaved-change detection must compare
order positionally, not numerically — stored orders 5 and 7 are perfectly arranged, and comparing
them against a freshly renumbered 10 and 20 would claim unsaved changes the moment the page opens.
The server receives the whole arrangement and most of it is usually already true, so ApplyOutcome
counts what actually changed, not what was sent. The superseded single-row procedures stay on the
wire because removing a procedure breaks a deployed client this repo cannot see, but nothing in this
product calls the writing ones any more; new admin edits belong on the staged apply path, not on new
single-row writes.
