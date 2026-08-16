# 0016. The template ships one example module, and it is Notes

Date: 2026-08-15
Status: accepted

## Context
The repository grew as a product, so it carries four feature surfaces a boilerplate does not owe
anyone: the Notes module, the Activity feed, the role-and-permission screens, and the
navigation-layout editor. A person starting from Kakehashi has to delete three of them before
writing a line of their own, which is the difference between a sample app and a starting point.
`docs/pivot/01-PHASE-0-INVENTORY.md` D1 recommends keeping only Notes and moving the rest to a
`showcase` branch.

## Decision
Template v1 ships `notes` as its single example. `activity` and the administration screens leave in
Phase 1 for a `showcase` branch, which is a branch rather than a deletion so the work stays
reachable and buildable. Notes earns the place because one module demonstrates the whole frame:
proto contract, the server's five packages, an event on the bus, the client's three layers, the
mediator, and a CRUD page. Both departures are already cut as removable units — `activity` has its
marker regions, `admin-ui` is drafted in [docs/BOILERPLATE.md](../BOILERPLATE.md) — so Phase 1
writes two unit files rather than performing a refactor.

The *mechanisms* those screens drive stay: `authz` enforces permissions, `account` issues tokens,
`navigation` serves the pane. Only the screens go. Whether the server's administration surface
(`account/rpc/admin.go`, `authz/service/admin.go`, `navigation/service/admin.go` and their
siblings) leaves with them is not decided here — it needs the dependency pass that writing the
`admin-ui` unit file forces, and guessing now would put a wrong list in a machine-read file.

## Consequences
`kakehashi new` ships one example and `--bare` removes it, so the two shapes a user can ask for are
both real. A scaffolded project has no administration UI, which is the honest default: the roles a
product needs are its own. The `showcase` branch must be rebased onto the template or it rots —
Phase 5 owns that, and a branch nobody rebases is worse than a deleted one. The gates never see a
weaker rule: `archlint` and the architecture tests run on the template with one module exactly as
they ran with four.
