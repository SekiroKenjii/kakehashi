# 0003. Signing out and being revoked are two events

Date: 2026-08-12
Status: accepted

## Context

SignOut and RevokeSession are the same session delete, and they used to be one method and one
published event. The account module's audit trail recorded every departure as EventSessionRevoked,
so the account page said a session had been revoked every time somebody signed out; the bus carried
one event for both facts, so the activity feed said "signed out" whichever had happened. And an
administrator ending somebody else's session announced nothing at all, so the one screen a person
opens to ask whether anyone has been in their account could not show it.

## Decision

accountapi.Service keeps SignOut and RevokeSession as separate methods although both perform the
same delete, because only the caller knows which one happened. SignOut records EventSignedOut and
publishes accountapi.SignedOut; the revocation paths record EventSessionRevoked and publish
accountapi.SessionRevoked, with ByAdmin true only on the administrative route. The activity feed
subscribes to both and writes KindSignedOut, KindSessionRevoked, or KindSessionRevokedByAdmin —
the admin case is its own kind because the client picks a label and an icon by kind.

## Consequences

A reader can tell "you signed out", "you ended a session", and "somebody else ended your session"
apart; the last is the only feed row that reports another person acting on the account, and ByAdmin
is honest because it comes from a different method behind a different permission, not a guess. The
cost is two methods with identical mechanics: a new sign-out path must call the right one, because
the method chosen is the only place the distinction exists. Two invariants must hold. Ending a
session that is already gone succeeds but announces nothing — an unconditional publish let any
session id write "somebody else ended your session" into a feed. And the kind strings are wire
contract: the client switches on them, so renaming one degrades the row to showing the raw value.
