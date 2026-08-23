# 0025 — A plugin's declared permission is disclosure, not a gate

## Context

A plugin's manifest is read before any of its code runs, which makes it the only thing the install
prompt can describe the package with. It would be natural to let a plugin declare the permission its
screens need and to have the host enforce that declaration.

[0015](0015-module-attachment-is-not-a-security-boundary.md) already settled the general form of
this: nothing stored client-side may be read as authorization, and client-side gating is a lock
drawn instead of a button that would fail. A plugin's manifest is the most client-side thing there
is — a file the user chose, in a package the user supplied.

There is a second problem, and it is not about security at all. A permission key a plugin invents is
not in the server's catalogue, so `IPermissionService.Allows` answers false for it. A plugin that
set `RequiredPermission` from its manifest would hide its own screen on every machine, and the
symptom — an installed plugin whose page never appears — points at nothing.

## Decision

**A plugin's navigation items leave `RequiredPermission` empty.** The screens a plugin adds are
visible to whoever installed it, and the server refuses whatever its code is not entitled to ask
for, at the one place that sees every request.

**The manifest field is named `callsPermission`, and what it is for is disclosure.** If it is shown,
it belongs beside the author, the digest and the screens the package adds — as a description of what
the package will be talking to, never as a claim that the host will stop it.

## Consequences

The field is currently parsed and not displayed: the prompt lists the author, the digest, the
signature, the screens and the host version, and no permission row. So the decision holds in the
direction that matters — nothing treats it as a gate — and the disclosure half is unbuilt rather
than wrong.

The install prompt is honest about what it is: a description of a package, and a decision about
whether to run code. It is not a policy engine, and nothing on it should be read as one.

A plugin screen cannot be rearranged from the Navigation admin page. Plugin items carry an empty
`NavigationItem.Id`, because a non-empty one is dropped the moment a server layout arrives — that
screen reconciles destinations declared by *server* modules, and a client-only plugin has none. An
empty id is always appended and keeps its own group. The cost is stated in `docs/PLUGINS.md` rather
than worked around.

A future deployment that wants plugin screens under real permissions has one honest route: the
server's catalogue has to learn the key, through the `plugins` module that published the package.
Until it does, the answer is that the host does not gate what it cannot verify.
