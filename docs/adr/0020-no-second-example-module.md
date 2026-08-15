# 0020. No second example module; Notes already touches the event bus

Date: 2026-08-15
Status: accepted

## Context
The frame has two facilities a single CRUD module might not demonstrate: the in-process event bus,
and MongoDB alongside SQL Server. The `activity` module demonstrates both, which is an argument for
keeping it as a second example rather than sending it to `showcase` under
[0016](0016-one-example-module-in-the-template.md). `docs/pivot/01-PHASE-0-INVENTORY.md` D5
recommends no second module for v1, and adding a small event to Notes if the bus needs a witness.

## Decision
One example module. The bus needs no addition: `notes/service` already publishes `notesapi.Created`,
`Updated` and `Deleted` after each write, and `notes/api` declares the event types beside the
interface — the pattern a reader has to copy is there in full. Nothing in the template subscribes to
them once `activity` leaves, which is honest rather than a gap: a published event with no subscriber
is exactly what a module's own events look like on the day it is generated.

MongoDB stays wired in `platform/mongodb` and unused by any shipped module. `kakehashi add module
--store mongo` is what demonstrates it, in the project that asked for it, rather than a permanent
second example every project has to read past.

## Consequences
The template ships one document store nothing writes to, so `docker compose up` starts a Mongo
container a bare project does not need. That is the price of `--store mongo` working without a
second scaffold path, and it is one container. A reader learning the event bus reads `notes/api`
and the three `Publish` calls, with no subscriber to read; the subscriber pattern lives in
`showcase`, where `activity/subscriptions.go` is a worked example. If v2 ships a second module it
should be the subscriber side, because that is the half this decision leaves undemonstrated.
