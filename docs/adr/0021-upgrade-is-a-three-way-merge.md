# 0021 — `upgrade` is a three-way merge, not a re-scaffold

## Context

A scaffolded project diverges from the template the moment somebody works on it. The template keeps
moving: a fixed migration, a new marker section, a dependency bump. Today the only way to take one
of those is to read the template's diff and apply it by hand, which nobody does after the first
month.

Two shapes were considered and rejected.

**Re-scaffold and overwrite** is what a project generator that keeps no state has to do. It cannot
tell the developer's edits from the template's, so it either destroys work or refuses to touch
anything that changed — and in a project of any age, everything has changed.

**Vendoring the template as a dependency** — the framework shape — would make upgrades a version
bump, but it is the opposite of the point. The value here is that the code is yours, in your
repository, editable. A project that cannot edit its own composition root is not a boilerplate.

## Decision

`kakehashi upgrade` is a **three-way merge**, in the shape `cruft` and `nx migrate` use.

The CLI has everything it needs to reconstruct the *unmodified* project at any two template
versions, because `.kakehashi.json` records the template version and every input the scaffold
consumed. So:

1. Scaffold a virtual project at the **old** template version with the recorded inputs. This is the
   common ancestor: what the project looked like before anybody touched it.
2. Scaffold a second virtual project at the **new** version with the same inputs.
3. Diff the two. That diff is the template's change, spelled in the project's own names.
4. Apply it to the real working tree as a patch.
5. Where the patch does not apply, write conflict markers and stop. A conflict is a decision, and
   the developer is the one who can make it.

Neither virtual scaffold is ever written where the developer can see it, and neither is compared
against the working tree directly — only against each other.

**The invariants v1 has to hold for this to be possible**, and which is why they are stated here
rather than in v2:

- `.kakehashi.json` records the template version, the inputs and the unit lists. Every new input
  belongs there on the day it is added, or a reproduction silently differs.
- `add module` writes a unit record, so a generated module is as reconstructible as a shipped one.
- **The marker and unit formats never change without `markersSchema` / `unitsSchema` going up.** A
  reconstruction at an old version has to be byte-identical to what that version produced; a format
  that changed underneath it produces a diff of the whole tree.
- Substitution stays literal and order-dependent. A change to the replacement table changes every
  reconstruction of every version.

## Consequences

An upgrade is a patch a developer reviews, not a thing that happens to them. Conflicts are visible
in the working tree in the form every developer already knows, and `git diff` afterwards is the
upgrade.

Reproducibility becomes a property the scaffold has to keep, which is a cost paid on every change to
the scaffold rather than once. The golden-tree tests are what make it observable: a scaffold that
stops being deterministic fails them before it reaches a release.

The manifest becomes load-bearing. A project that deletes `.kakehashi.json` can never be upgraded —
its ancestor is unrecoverable — and the file is documented as belonging in version control.

Two template versions must be fetchable at upgrade time, so `upgrade` needs the network or a warm
cache, and the release archives for old versions have to keep existing. Deleting an old release
breaks the upgrade path from it.

Nothing about this ships in v1. What v1 ships is the manifest, the unit records, and the schema
numbers that keep them readable — the parts that cannot be added retroactively, because they have to
have been recorded at scaffold time.
