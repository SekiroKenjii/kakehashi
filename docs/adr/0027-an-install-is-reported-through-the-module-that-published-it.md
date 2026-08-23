# 0027 — An install is reported through the module that published it

## Context

An install is worth a row in the activity feed: it is the kind of thing whose answer to "was that
me?" could be no, and a plugin runs with everything the application can do.

Only the client knows an install happened. The obvious route is the one write path a client already
has — `RecordClientEvent` on the activity module — which would mean adding a plugin kind to
`activityapi.CanReport`.

That file warns about exactly this. `CanReport` is a closed two-kind allow-list, and its comment
asks the question first: could a compromised client use this to tell a lie a reader would act on?
For an install the answer is yes. A client that could report "installed from the catalog" could say
so about a package this deployment never offered, and the row it wrote would be the one that made a
reader relax.

## Decision

**The client calls `PluginService.ReportInstalled` on the `plugins` module**, which validates
against its own catalog before it records anything, and publishes `pluginsapi.Installed` on the
event bus. The activity module subscribes to that event exactly as it subscribes to the account
module's, and writes the row.

`activityapi.CanReport` stays at two kinds. The security decision its comment warns about is not
taken.

The request decides almost nothing: the account comes from the token, the time from the server's
clock, and a version this catalog does not hold is refused. The source is checked against a closed
set, and the refusal names none of it.

## Consequences

The source becomes the kind rather than a field on the row, because it is the difference a reader
acts on. `PluginInstalled` is a package this deployment's own catalog offered and sits under System;
`PluginSideloaded` is anything else and sits under Security, drawn as an alert. A source a later
build adds reads as the louder kind, which is the direction a wrong guess has to fail in.

Neither row names the plugin. An entry has no field for one, and adding it would reach the domain,
the store, the contract and the client for a fact the Plugins screen already states as an inventory.
What the feed answers is "did I do that, and when"; what was installed is a different question, on a
different screen.

A sideloaded package that this catalog has never published cannot be reported at all — the server
refuses a version it does not hold. That is the cost of the closed check, and it is the right way
round: the feed under-reports rather than carrying a row nobody can corroborate.
