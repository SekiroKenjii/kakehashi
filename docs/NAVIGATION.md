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
- **`NavigationAdminService`** — the layout surface: `ListGroups`, `CreateGroup`, `UpdateGroup`,
  `DeleteGroup`, `ListItems`, `MoveItem`, `UpdateItem`. Gated **once, on the route**, by
  `navigation.manage`, so every procedure added later inherits the check.

`navigation.manage` is its own permission rather than `roles.manage`: arranging a pane and handing out
access are different jobs, and somebody trusted to tidy the navigation need not be trusted to grant
permissions.

Note what is absent from every write: there is no field anywhere on the admin surface that carries a
permission. `ItemConfig` reports `required_permission` and `hide_when_denied` so a screen can explain
why something is invisible to a colleague, and nothing can set them.

## Declaring a destination

One list, at the composition root, because that is the file that already knows what this build is
made of:

```go
var navigationLayout = navigation.Options{
    SystemGroups: []navigationapi.SystemGroup{
        {ID: "utilities", Title: "Utilities", Order: 10},
        {ID: "administration", Title: "Administration", Order: 20},
    },
    Destinations: []navigationapi.Destination{
        {ID: "notes", ModuleID: "notes", DefaultTitle: "Notes", DefaultIcon: "note",
            DefaultGroup: "utilities", DefaultOrder: 10},
        {ID: "account.users", ModuleID: "account", DefaultTitle: "Users", DefaultIcon: "people",
            DefaultGroup: "administration", DefaultOrder: 10,
            Permission: accountapi.PermissionManageUsers, HideWhenDenied: true},
    },
}
```

**Destinations, not modules.** One module can own several: the account module owns both the caller's
own Account screen and the administrative Users directory, which sit under different headings and
answer to different permissions. A table keyed by module could not place both.

**The `Default*` fields are seeds.** Reconcile writes them the first time a deployment sees a
destination and never again. Changing one affects new deployments and new destinations, and nothing
else.

**`Permission` empty means the owning module's `.access`** — the same permission the route gate checks,
so a screen is locked exactly when its endpoints are. A destination owned by an **ungated** module
(`health`, `account`, `authz`, `navigation`) must name its permission instead: nobody holds `.access`
for a module whose routes are never checked against it, and an empty one there would draw a row
disabled forever.

**`DefaultIcon` is a semantic name** — `note`, `people` — never a glyph. Which code point draws a note
is a fact about the font a client ships with; `NavigationGlyphs` maps the name, and an unknown name
falls back to whatever the page already declared.

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

## Two WinUI traps this screen paid for

Both cost a live debugging session and neither produces an error.

**`ItemsRepeater` does not set `DataContext` on the elements it realises.** That is part of what makes
it lighter than a `ListView`. A `SelectionChanged` handler reading `sender.DataContext` matched
nothing and returned silently on every interaction — the controls looked like they worked, because
their `x:Bind` bindings did. Read the row from `Tag="{x:Bind}"` instead.

**A `ComboBox` raises `SelectionChanged` when its own item list is replaced, and again when the
repeater recycles the row onto a different destination.** Both look exactly like somebody choosing
something, and the first version of this screen duly moved every row it drew. Three things together
fix it: every row binds its **own snapshot** of the choices so no bound collection is ever cleared; a
guard compares against what the server last confirmed for **that** row; and a change reporting **no
selection at all** is ignored, because "No heading" is a real item whose id happens to be empty, while
an unbound picker has no item.

`DropDownClosed` looks like a tidier answer and is not: a keyboard user changes a closed combo box
with the arrow keys and never opens a dropdown, so the write would never happen for them.
