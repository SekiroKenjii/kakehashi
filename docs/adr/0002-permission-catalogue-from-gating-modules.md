# 0002. The permission catalogue lists only permissions some route checks

Date: 2026-08-12
Status: accepted

## Context

The catalogue of grantable permissions is declared by modules and reconciled to the database at
boot, so a permission nothing enforces cannot survive as a row. Module access broke that rule: the
authz module minted one `<id>.access` permission per mounted module, from a module list passed to
its constructor — a second copy of the mount list that only a test kept in step. Four mounted
modules never check their `.access` (authz's, health's, and account's routes are ungated by design,
named in cmd/server/main.go), so those rows were official-looking permissions that granted nothing.

## Decision

`Kernel.AccessModules()` walks the collected route table and returns, in mount order, only the
modules that actually gate a route on their own `<id>.access`. The authz module builds its
catalogue from that answer plus the finer permissions modules declare through `authzapi.Catalogue`;
its constructor takes no list. Assembly happens in `Finalize` rather than `Start`, because no
module's routes are all collected until every module has started.

## Consequences

Every catalogue row is checked by some route or handler, so granting a listed permission always
changes behavior, and boot reconciliation removes rows nothing claims. The duplicated mount list is
gone. Two invariants must hold: the catalogue depends on the route table, so it cannot be assembled
before `Finalize`; and `Kernel.Routes()` collects routes exactly once — a module's `Routes()`
builds handlers, so collecting twice would hand the mux one handler set and the access question a
different, unmounted set. A module that begins gating on `.access` enters the catalogue
automatically; one that stops has its row reconciled away at the next boot.
