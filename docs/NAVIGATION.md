# Navigation

The navigation pane is not compiled into the client. It is computed per request from two sources, and
the split between them is the whole design.

| Question | Answer lives in | Why there |
| --- | --- | --- |
| Which destinations **exist** | Code (`cmd/server/main.go`) | A destination is a compiled page behind a permission. No row in a table can conjure one. |
| What **protects** each one | Code (same declaration) | A security invariant. If an administrator could edit it, one wrong click opens an API. |
| Which heading, what **order**, what **label**, offered at all | Database (`navigation.*`) | Presentation. Renaming a heading should be an afternoon, not a release. |
| Headings themselves (create / rename / reorder / delete) | Database | The requirement: no deploy. |

Said the short way: **the database decides how the app is arranged, the code decides what it
protects.** Those two axes never touch, which is what makes handing the arrangement to an
administrator safe — the worst a mistake there can do is hide something.

## The contract

`proto/kakehashi/navigation/v1/navigation.proto`, two services:

- **`NavigationService.GetNavigation`** — the caller's own pane. Open to any signed-in caller, and it
  has to be: a client needs its pane before it can draw anything, so an account with no grants must
  still be able to ask what it may see. `navigation` is therefore in `ungatedModules`.
- **`NavigationAdminService`** — the layout surface. Reads: `ListGroups`, `ListItems`,
  `PreviewLayout`. Writes: `ApplyLayout` and `DeleteItem`. Gated **once, on the route**, by
  `navigation.manage`, so every procedure added later inherits the check.

  The single-row writes it began with — `CreateGroup`, `UpdateGroup`, `DeleteGroup`, `MoveItem`,
  `UpdateItem` — are still on the wire and still work. They are kept because removing a procedure
  breaks a client this repo cannot see, and the comment above `ApplyLayout` says plainly that nothing
  in this product calls them any more.

`navigation.manage` is its own permission rather than `roles.manage`: arranging a pane and handing out
access are different jobs, and somebody trusted to tidy the navigation need not be trusted to grant
permissions.

Note what is absent from every write: there is no field anywhere on the admin surface that carries a
permission. `ItemConfig` reports `required_permission` and `hide_when_denied` so a screen can explain
why something is invisible to a colleague, and nothing can set them.

## Declaring a destination

Each module declares its own, by implementing `navigationapi.Contributor` in a `navigation.go` beside
its `module.go`:

```go
// server/internal/modules/account/navigation.go
func (m *Module) NavigationDestinations() []navigationapi.Destination {
    return []navigationapi.Destination{{
        ID:             "account.users",
        DefaultTitle:   "Users",
        DefaultIcon:    "people",
        DefaultGroup:   "administration",
        DefaultOrder:   10,
        Permission:     accountapi.PermissionManageUsers,
        HideWhenDenied: true,
    }}
}
```

This used to be one list at the composition root, and moving it was the point of a change the
composition root asked for: `main.go` should not grow a line every time somebody adds a screen. What
the root still owns is which modules are mounted at all.

**`ModuleID` is not set here.** The navigation module stamps it from `module.ID()` while collecting, so
a module cannot declare a screen as belonging to another one.

**The headings the product ships** are a package var in `navigation/module.go` — `utilities` and
`administration` — because they are the navigation module's own vocabulary rather than any feature's.

**Three things fail the boot**, checked in `collect` once every module has started:

- two modules claiming the same destination id;
- a `DefaultGroup` naming a heading this build does not ship;
- an empty `Permission` on a module that does not gate a route on its own `.access` — see below.

**Destinations, not modules.** One module can own several: the account module owns both the caller's
own Account screen and the administrative Users directory, which sit under different headings and
answer to different permissions. A table keyed by module could not place both.

**The `Default*` fields are seeds.** Reconcile writes them the first time a deployment sees a
destination and never again. Changing one affects new deployments and new destinations, and nothing
else — which is why `default_group` and `default_order` are also *reported* on the admin surface: once
somebody has moved a screen, the arrangement the product shipped is otherwise unrecoverable through
any API.

**`Permission` empty means the owning module's `.access`** — the same permission the route gate checks,
so a screen is locked exactly when its endpoints are. A destination owned by an **ungated** module
(`health`, `account`, `authz`, `navigation`) must name its permission instead: nobody holds `.access`
for a module whose routes are never checked against it, and an empty one there would draw a row
disabled forever.

**`DefaultIcon` is a semantic name** — `note`, `people` — never a glyph. Which code point draws a note
is a fact about the font a client ships with; `NavigationIcons` (in `Kakehashi.UI.Common`) maps the
name, and an unknown name falls back to whatever the page already declared.

## Reconciliation

Runs at boot, after the migrations and before the server serves. Three cases:

| Case | What happens |
| --- | --- |
| A destination with no row | Seeded from its declared defaults, so a new module appears in the pane the moment it is deployed. |
| A destination **with** a row | Left completely alone. |
| A row with no destination | Left alone, marked an orphan, skipped when the pane is built. |

The middle case is the one worth guarding. A version of `Reconcile` that also refreshed titles and
groups would undo every rearrangement on every restart — silently, and in production, where restarts
happen unattended. There is a test named after exactly that.

Orphans are kept rather than deleted so a module that comes back — a rollback, a flag turned on again
— comes back where somebody put it. They appear on the Navigation screen, which is the only place
anybody can find out one exists. There is no cleanup job; deleting a stale row is a deliberate act.

## Building a caller's pane

`service.Build(ctx, grants)` reads placement from the database and access from the grants the route
gate already resolved onto the request. It decides nothing about access itself — two code paths that
could disagree about what a caller may do is how a client ends up drawing an unlocked door onto a
locked room.

| Outcome | Pane |
| --- | --- |
| Permitted | present, reachable |
| Denied, `HideWhenDenied` off | present, **disabled** |
| Denied, `HideWhenDenied` on | **absent** |
| Hidden by an administrator (`is_visible`) | absent, for everybody |

Disabled is the default because a product having something this account has not been given is worth
being able to see and ask for — the same argument a server makes by answering 403 rather than 404.
Hiding is for destinations where the existence is itself administrative: a user directory, a
permissions matrix.

**A heading whose every destination came out absent is dropped, not sent empty.** That is the fix for
the reported bug where "Administration" appeared, disabled, to an account with nothing under it.

## Applying a layout

`ApplyLayout` takes the **whole desired arrangement** and writes all of it or none of it. It replaced
six procedures that each changed one row.

That the six were right once is worth being precise about, because a comment in this repo argued for
them: when every edit is applied the instant it is made, one call *is* one change, and a transaction
has nothing to protect. What made the argument expire was the gesture, not the opinion. Dragging a
screen into another heading renumbers what it lands among, so one gesture is several rows — and a
sequence of single-row calls cannot fail halfway without leaving the pane half-rearranged. It already
did: a reorder was two `MoveItem` calls, and a failure on the second left both rows holding the same
number.

So the shape is authz's `SaveRoleGrants` — desired state in, an outcome summary out
(`groups_created`, `groups_updated`, `groups_deleted`, `items_changed`):

| Sent | Meaning |
| --- | --- |
| A group with an id | Update it |
| A group with **no** id | Create it, slug from the title |
| A group **absent** from the request | Delete it — system headings still refuse |
| An item, matched by id | Place and label it as described |
| An item **absent** from the request | **Leave it alone** |

The asymmetry in the last two rows is deliberate. Groups exist because an administrator made them, so
absence can mean "no longer wanted". Items exist because a module compiled a page, so absence far more
likely means a client that sent a partial request — and deleting a row on that basis would lose a
placement over a bug. `DeleteItem` is the way an item leaves, and it only accepts orphans.

**Everything is validated before anything is written**, in `service/apply.go`: slugs, title length,
duplicate titles, unknown groups, and any attempt to hide a `HideWhenDenied` destination. A refusal
therefore changes nothing, which is what lets the screen keep the administrator's unsaved work on the
error path instead of reloading over it.

One case is worth naming because a test caught it: an item filed under a heading the same request
deletes. That is not an error — the schema's `ON DELETE SET NULL` already does this for the
single-row `DeleteGroup` path, so refusing it here would make the two delete paths disagree about the
same situation. The item falls to ungrouped.

The cache is invalidated **once**, after the transaction commits, rather than per row.

## Previewing somebody else's pane

`PreviewLayout(role_id)` builds the pane as it would be for a role other than the caller's. Most
screens are behind a permission, so an administrator holding everything is the one person who cannot
see what they just arranged.

It needs the role's grants, which belong to `authz` — so `authzapi` grew `GrantsForRole`, and that
package's doc now carries the caveat that matters more than the function: **read these to explain and
to draw, never to decide.** The route gate stays the only thing that resolves grants for an actual
request. Two code paths that could disagree about what a caller may do is how a client ends up
drawing an unlocked door onto a locked room.

## Code-owned or administrator-owned

The one table to check before adding a field to this surface:

| Property | Owner | Editable at runtime |
| --- | --- | --- |
| The destination exists at all | Code (`navigationapi.Contributor`) | No |
| `Permission`, `HideWhenDenied` | Code | **No** — a security invariant, and no write on this service carries either |
| `ModuleID` | Code — stamped by the navigation module | No |
| Route | Code — the client's, resolved from the page type | No |
| `DefaultGroup`, `DefaultOrder`, `DefaultTitle`, `DefaultIcon` | Code | No — seeds, written once by `Reconcile`, but **reported** so a moved screen can be put back |
| Which heading, what order | Database | Yes |
| `Title`, `Icon` overrides | Database | Yes |
| `IsVisible` | Database | Yes, except where `HideWhenDenied` decides it |
| Headings — create, rename, reorder, delete | Database | Yes |

## Schema

SQL Server, `navigation` schema, one migration:

- **`navigation.NavGroup`** — `Id` (slug), `Title` (unique), `SortOrder`, `IsSystem`.
- **`navigation.NavItem`** — `Id` (the destination), `ModuleId`, `GroupId` (nullable, `ON DELETE SET
  NULL`), `Title`/`Icon` (nullable overrides), `SortOrder`, `IsVisible`.

`ON DELETE SET NULL` rather than `CASCADE`: deleting a heading is a decision about the heading, and
the destinations under it are still compiled into the client. They fall to ungrouped and wait to be
filed again, which loses an administrator one placement instead of a page.

`NULL` and `''` mean different things in the override columns. `NULL` is "whatever the code calls it",
so a page that gets renamed carries its new name everywhere nobody deliberately renamed it.

System headings can be renamed and re-ordered but never deleted: a deployment that deleted the
heading its administrative screens live under would have nowhere left to put them. The refusal carries
a sentence, not a bare 400.

## The client

`INavigationLayoutService` fetches the pane at sign-in (order 17, beside the permission fetch, because
the pane needs both answers before the shell is built at 20) and again after any layout write, so an
administrator sees the pane change immediately rather than at the next sign-in.

`NavigationPlanner` joins the deployment's arrangement to what this build actually has. The
deployment decides the menu; the client contributes three things only it can know:

- whether it **has** the destination — a layout naming a page this build does not contain is skipped,
  because there is no page to open;
- whether the user **detached** its module — their own preference about their own composition, and no
  server is entitled to overrule it;
- where the **footer** items go — the account avatar is not in the menu, so nothing about it is the
  deployment's to arrange. An item with no `Id` was never offered for arrangement.

With no layout — first run, or an unreachable server — it falls back to the arrangement compiled into
the build. An app that will not draw a menu because a call timed out is worse than one drawing the
menu it shipped with.

### The Navigation screen edits a copy

Every edit on that screen is held in memory until somebody presses Apply. A node keeps the values the
server last confirmed beside the values being edited, so "modified" is something it can answer about
itself rather than something a counter has to be kept in step with, and Discard is a copy back rather
than a reload.

Three things follow, and they are the argument for the model:

- **the pane preview is honest.** It runs the real `NavigationPlanner.Plan` over the staged
  arrangement, so what the preview draws is what applying would produce — not an illustration of it;
- **a refused Apply loses nothing.** The staged state is still there, with the error above it;
- **the renumbering is a consequence, not a feature.** Order is assigned by position at the moment the
  request is built, and only for the headings whose sequence actually changed. The old defect where a
  moved row carried a stale `SortOrder` and landed in an arbitrary place cannot be expressed.

The screen also refuses locally what the server would refuse: the eye on a `HideWhenDenied` screen
does not offer to hide it and says why, rather than letting somebody click and read an error bar.

Two things the screen deliberately does **not** stage: `DeleteItem` on an orphan, which is a
single irreversible act better done immediately than accumulated, and `PreviewLayout`, which is a
read.

## Three WinUI traps this screen paid for

All three cost a live debugging session and none produces an error.

**Repeating controls do not set `DataContext` on the elements they realise** — `ItemsRepeater` does
not, which is part of what makes it lighter than a `ListView`. A handler reading `sender.DataContext`
matched nothing and returned silently on every interaction; the controls looked like they worked,
because their `x:Bind` bindings did. Every row on this screen carries `Tag="{x:Bind}"` and every
handler reads its row from there. That is why the rebuilt page can be nested `ItemsControl`s and
still be sure which row a click came from.

**A `ComboBox` raises `SelectionChanged` when its own item list is replaced.** That looks exactly like
somebody choosing something, and the first version of this screen — which had a picker per row, inside
a repeater — duly moved every row it drew, twice over: once when the list was bound and again when the
repeater recycled the row onto a different destination.

The rebuild removed the row pickers in favour of one picker in the editor panel, which removes the
recycling half of the problem and not the other half. What remains is handled the way the old screen
handled it: the picker's selection is **assigned** when the selection changes rather than two-way
bound, under a flag that says the assignment is the screen's own doing. The reason two-way binding is
wrong here is worth keeping written down — a rebuilt item list writes `null` back through the binding,
and "no heading" is a real choice whose id happens to be empty, so a rebuild reads as somebody
unfiling the screen.

`DropDownClosed` looks like a tidier answer and is not: a keyboard user changes a closed combo box
with the arrow keys and never opens a dropdown, so the change would never register for them.

**A drag needs something in the package or it never starts**, and what this screen drags is an object
rather than a string. So the package gets the title as text — enough for the drag to begin — and the
row being dragged is held in a field the drop handler reads. Putting an identifier in the package and
looking it back up would also work, and would also mean the drop handler could be handed an identifier
from another window.

Drag and drop is not a keyboard path, so the chevrons stayed. They are not a fallback for the mouse;
they are the only way this screen can be rearranged without one.
