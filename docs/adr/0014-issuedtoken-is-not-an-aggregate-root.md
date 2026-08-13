# 0014. IssuedToken lives inside UserSession, not as its own root

Date: 2026-08-12
Status: accepted

## Context

The account module's domain package defines four aggregate roots — Account, UserSession,
AuthRequest, and SigningKey — each in its own file with its own lifecycle and consistency
boundary. When token issuance was modelled, IssuedToken could have become a fifth root beside
UserSession. But a token has no life of its own: it is issued under a session, it means nothing
without one, and ending the session must end its tokens. That is a consistency rule, not a
cleanup preference, so the two types cannot sit in separate consistency boundaries.

## Decision

IssuedToken is an entity inside the UserSession aggregate (session.go), not an aggregate root.
The session is the consistency boundary; tokens are reached and revoked only through it. The
store mirrors the invariant with a foreign key from issued tokens to sessions declared
ON DELETE CASCADE, so the database enforces it even when a delete arrives from a path the
service never sees.

## Consequences

- Ending a session cannot strand live tokens: the rule holds in the domain model and, via the
  cascade, in the schema — future migrations must not drop or weaken that foreign key.
- Tokens cannot be created, loaded, or revoked independently of a session; any operation that
  needs a token goes through its owning UserSession.
- The package carries a test for the next addition: a type is a root only if it can be deleted
  on its own without leaving something else in a state its own rules forbid. If it cannot, it
  belongs inside an existing root rather than beside it.
