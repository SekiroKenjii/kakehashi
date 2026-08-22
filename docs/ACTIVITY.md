# Activity

What has happened to an account, collected on the server and readable from any device.

It is the feature that justifies the server. The client's desktop ancestor wrote sign-ins into local
settings, so the only machine that knew about a sign-in was the machine that performed it. Here the
entries live server-side, which is the whole difference: sign in on one device and the next device you
open already knows.

## Where entries come from

Almost entirely from the server watching itself. The activity module subscribes to what other
modules publish and writes one row per fact; `server/internal/modules/activity/subscriptions.go` is
the only file in the module that imports another module, and the only one anybody edits when the
feed's scope changes.

| Kind | Category | Published by |
| --- | --- | --- |
| `SignedIn` | SignIn | `account/service/signin.go` |
| `NewDeviceSignedIn` | SignIn | the same event, with `NewDevice` set |
| `SignedOut` | SignIn | `SignOut` — the account holder leaving |
| `SessionRevoked` | SignIn | `RevokeSession` / `RevokeAllSessions` — the owner ending one |
| `SessionRevokedByAdmin` | Security | `RevokeAccountSession` — somebody else ending it |
| `FailedSignIn` | Security | either refusal in `Authenticate` |
| `PasswordChanged` | Security | `account/service/profile.go` |
| `AppUpdated` | System | the client, through `RecordClientEvent` |
| `ThemeChanged` | System | the client, through `RecordClientEvent` |
| `PluginInstalled` | System | `plugins/service/install.go` — a package this catalog offered |
| `PluginSideloaded` | Security | the same event, with any other source |

Three of these are worth knowing the history of, because the feed was wrong about them until
recently:

- **`SignedOut` and `SessionRevoked` were one event.** Leaving and being ended are the same delete, and
  only the caller knows which happened — so the service now has two methods where it had one, and the
  feed says which.
- **`SessionRevokedByAdmin` reached the feed not at all.** An administrator ending somebody's session
  was written into that account's own audit trail and announced to nobody, so the screen a person opens
  to ask whether anyone has been in their account was silent about the one event most worth its silence
  being broken.
- **`FailedSignIn` and `NewDeviceSignedIn` existed in `accountapi` and were never published**, so the
  feed showed the coarser `SignedIn` where the account page said "New device signed in".

**A plugin install is two kinds because its source is the difference a reader acts on.** The event
carries where the package came from, and only this deployment's own catalog earns the quieter row —
everything else is code nobody here chose to offer, running with everything the application can do.
A source a later build adds reads as `PluginSideloaded`, which is the direction a wrong guess has to
fail in. Neither row names the plugin: an entry has no field for one, and the Plugins page is the
inventory. What the feed answers is "did I do that, and when".

**An event is published only when the write it describes actually did something.** Ending a session
that is already gone is deliberately idempotent, and for a while it announced a revocation anyway —
which meant an administrator passing any session id at all, existing or not, could put "somebody else
ended your session" into an account's feed. Live verification found it. The three session paths now
read what the delete affected before they say anything, and the delete still succeeds either way.

## The one write from outside

`RecordClientEvent` is the deliberate exception: the one write on an otherwise read-only module. An
endpoint that let a caller freely append to their own history would be an endpoint that lets a
caller rewrite it, which is why this write is shaped so the request decides almost nothing.

What makes a history worthless is a caller who gets to say what happened. This one does not:

| Decided by | What |
| --- | --- |
| the token | whose feed it is — there is no account id in the request |
| the server's clock | when it happened |
| the connection | the device and the address |
| a closed allow-list | what kind of fact it is |

`activityapi.CanReport` is that list, and it holds exactly two kinds. Nothing on this server can
observe which build somebody is running or what theme they set, so those two facts arrive from outside
or the feed does not have them. Anything else is `INVALID_ARGUMENT`, and the refusal names no kinds —
a refusal that listed what is allowed would teach a caller what else to try.

Adding to that list is a security decision, not a feature decision. The question to answer first is
"could a compromised client use this to tell a lie a reader would act on?"

**The gap, named rather than waved away:** nothing throttles it. A compromised client can repeat what
it is allowed to report and dilute a feed with noise. Retention is by age rather than by count so
nothing is pushed out of existence, but that is a mitigation and not a defence.

## Reading it

One RPC, `ListActivity`, and everything the screen needs comes back with the page.

**Paging is keyset, never skip-and-limit.** New entries land at the head of this feed between any two
reads, which is exactly the case an offset gets wrong — page two of an offset repeats rows page one
already showed. The token is opaque base64 so its shape stays the server's business, and a token the
server cannot read is *refused* rather than ignored: starting over from the newest entry would draw
page one again under a "load more" button, and the reader would conclude the feed loops.

**The read asks for one row more than the page.** Whether there is another page is then something the
read observed rather than something a second query guesses at — a page that comes back exactly full is
otherwise indistinguishable from the last one.

**Counts are the server's, and they are over everything retained.** A chip has to show its own count
while a different chip is active, so the counting query drops the category filter and keeps the range
and the search. Counting the page that happens to be loaded would give numbers that changed as
somebody scrolled.

**Per-kind counts are sent as well as per-category ones**, because the two answer different questions:
a chip filters by category, and a summary card states one exact fact. "One sign-in was refused this
week" cannot be derived from a Security total that also holds password changes.

## Retention

Ninety days, in `domain.Retention`, enforced by a TTL index rather than by a job this server schedules.
A background sweep would be a second thing to deploy, to monitor and to get wrong, and it would have
to be careful not to run on every replica at once. The trade is that expiry is approximate — Mongo's
TTL monitor wakes about once a minute — which is exactly the precision "kept for ninety days" deserves.

Two consequences:

- **The TTL index is its own index.** Mongo honours a TTL only over a single date field, so it cannot
  be a flag on the compound index the reads use.
- **Append-only is not the same as permanent.** Nothing edits or deletes an entry; entries expire.
  Nobody can rewrite history — history simply stops going back forever, which is what the footer means
  when it says how far it goes.

**On first deploy this deletes anything older than ninety days.** It is the module's first deletion
path, and it runs without being asked.

## The storage

MongoDB, collection `activity_entry`, two indexes:

| Index | Keys | For |
| --- | --- | --- |
| `IX_Entry_UserId_OccurredAt_Id` | `user_id`, `occurred_at` ↓, `_id` ↓ | the match, the sort and the cursor, in one traversal |
| `IX_Entry_OccurredAt_TTL` | `occurred_at` | retention |

The equality field leads, so the sort is never done in memory. An index led by `occurred_at` would
serve "everyone's activity, newest first" — a query this module must never answer, because it has no
basis on which to authorize a cross-account read.

`kind` is deliberately **not** indexed. The counts group by it, but only after a match on `user_id` and
a date range the compound index serves completely, so the group runs over one account's window rather
than over the collection. An index on `kind` would earn its write cost only if something matched on
`kind` first, and nothing does: every read starts from "whose feed is this".

The store is one file, `store/entry.go`, because there is one collection: store/'s unit of
decomposition is the table or collection, and an axis with one value has nothing to split. There is
deliberately no `storable` truncation helper as in `notes/store`: the driver truncates timestamps to
milliseconds on encode and decodes them back as UTC, and nothing in this module observes it, because
the subscriber discards the entry after writing. The day an insert returns the stored entry to a
caller that compares it, the helper comes back.

## What the client decides

The server ships facts; the wording is the client's. That is what lets this feed be re-worded,
re-illustrated and localized without a server release — and what stops the server from owning
presentation for clients it cannot see.

Specifically the client owns:

- **the label and the icon** per kind, in `ActivityRow.Present`;
- **which day a moment belongs to** — the reader's local midnight, not the server's. A sign-in at 00:30
  in Ho Chi Minh City belongs under today for the person reading it, whatever UTC thinks;
- **which repeats are one event.** Nine sign-outs from one session inside fifteen minutes are one row
  with a `×9` badge. Only a *consecutive* run collapses: grouping every matching entry in the page
  would reorder the feed, and an entry with no session never collapses at all, because two password
  changes are two decisions rather than one event reported twice.

Two things the client deliberately does **not** do:

- **filter or search locally.** A page that filtered what it had already fetched would answer "no
  matches" for something three pages down;
- **count.** See above.

## Two honest reductions

The page follows `client/docs/mockups/activity-page-mockup.html`, and two of its elements are drawn
differently on purpose:

- **"Initiated by"** is absent. There is no actor field at any layer — the server records what happened,
  not who asked — and inventing one would be a fabrication on the single screen somebody opens to check
  whether a stranger has been in their account.
- **"Devices 2"** became "PLATFORMS IN VIEW". A count of distinct devices over the range does not exist:
  the server groups by kind and by category, not by device, and counting the loaded rows would give a
  number that grew as somebody pressed "load more". The card says what it actually counts.
