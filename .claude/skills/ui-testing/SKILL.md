---
name: ui-testing
description: Drive the built Kakehashi WinUI client through UI Automation with `winapp ui` — launch it against the live server, run the ui-tests.ps1 harness, read the results, and review the screenshots. Use before a release, after redesigning a page, or whenever a change needs proving in the running app rather than in a view-model test. Records the traps that cost a debugging session each.
---

# Driving the app through UI Automation

The 271 unit tests are view-model tests. They never construct a `Page`, which is the rule in
[CLAUDE.md](../../../CLAUDE.md) and the right one — but it means **no test in the repo has ever
drawn the UI**. Everything about focus, tab order, accessible names, drag and drop, layout and
clipping is unproven until the app is driven for real.

That is what this is for. `winapp ui` speaks UI Automation, the accessibility layer, so what it can
reach is very close to what a keyboard or a screen reader can reach. A control it cannot find is
usually a control a person cannot use.

## Before anything

```powershell
winapp --version              # 0.5.0 was used to write this
docker compose ps             # mssql, mongo and server must all be up
```

The client signs in against `http://localhost:8080` (`appsettings.json`, `Auth:Mode = InApp`), so
the server has to be running or the app opens on a sign-in window instead of the shell. If a refresh
token from a previous run is still on disk the app comes up already signed in, which is the usual
case and is fine.

## Build and launch

```powershell
dotnet build client\src\App\Kakehashi.App\Kakehashi.App.csproj -c Release -p:Platform=x64
Start-Process client\src\App\Kakehashi.App\bin\x64\Release\net10.0-windows10.0.19041.0\Kakehashi.App.exe
```

**Build the `.csproj` with `-p:Platform=x64`, not the solution.** The solution build writes
`bin\Release\`; the runnable `.exe` the launcher above uses is in `bin\x64\Release\`. Building the
solution and then launching the x64 path runs *the previous build* — which looks exactly like "my
fix did nothing" and has cost a full cycle here already. Check the timestamp when a result surprises
you:

```powershell
(Get-Item ...\bin\x64\Release\net10.0-windows10.0.19041.0\Kakehashi.App.exe).LastWriteTime
```

A running instance holds a lock on that `.exe`, so stop it before rebuilding:

```powershell
Get-Process -Name Kakehashi.App -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Run the harness

[ui-tests.ps1](ui-tests.ps1) exercises the shell, all seven pages, and the two screens most likely to
break. One pass, ~2 minutes.

```powershell
$p = Start-Process ...\Kakehashi.App.exe -PassThru
.\.claude\skills\ui-testing\ui-tests.ps1 -AppPid $p.Id
```

It prints PASS/FAIL per check, writes `test-results.json`, and drops a screenshot at each meaningful
state into `ui\` beside the script (override with `-ShotDir`). Exit code is non-zero if anything
failed.

**Then look at the screenshots.** UI Automation reports PASS on an app that is visually broken: it
sees names and patterns, not clipping, overlap, or a control bleeding past its container. Every
finding in the "what this has caught" list below that concerns layout came from the images, not from
an assertion.

## Selectors

`winapp ui` addresses elements by a *slug* (`btn-refresh-add6`) or by matching text against the
element's name or AutomationId.

**Slugs are regenerated whenever the visual tree is rebuilt.** They are stable within one page view
and worthless across a reload, a rebuild, or a restart. Nothing in the harness hard-codes one; every
selector is resolved at the moment it is used:

```powershell
$hits = (winapp ui search 'Refresh' -w $hwnd --json | ConvertFrom-Json).matches
$sel  = ($hits | Where-Object { $_.name -eq 'Refresh' -and $_.type -eq 'Button' }).selector
```

Matching by text is fine and is what the harness does, but a bare name is often ambiguous —
"Navigation" matches the pane item, the pane toggle button and the row on the Navigation screen. Add
the control type, and sort by `y` when several rows share a name.

## Traps

Each of these cost a debugging session.

| Trap | What happens | What to do |
| --- | --- | --- |
| `inspect --json` without `-i` | Answers with the window and no elements, so every filter finds nothing | Use `-i` for the flat element array; use the plain text form at `-d 24` when you need the `Text` runs too |
| A `Button` wrapping a row | A Button is a **leaf** in the automation tree: controls inside it disappear entirely | Make a row a focusable `Border` (`IsTabStop` + `KeyDown`), never a Button, when it contains its own buttons |
| `set-value` on a `ComboBox` | Refused — WinUI ComboBox has no settable UIA value | `invoke` it to expand, then `invoke` the item |
| A ComboBox dropdown | Opens in a separate `PopupHost` window, so `-w <main hwnd>` cannot see the items | Search and invoke with `-a <PID>` for anything inside a dropdown |
| `set-value` on a `TextBox` | Sets the text without raising `LostFocus`, so an `x:Bind` that commits on focus loss never fires | Prefer `send-keys --via send-input`; the repo's boxes use `UpdateSourceTrigger=PropertyChanged`, which is why this mostly does not bite here |
| Matching a count in the page text | The Activity day header ("37 events") sits above the footer ("Showing 50 of 176 events") | Anchor the pattern — `'Showing \d+ of \d+'` |
| A tooltip as the only label | `ToolTipService.ToolTip` is not an accessible name, and neither is `AutomationProperties.AutomationId` | Icon-only buttons need `AutomationProperties.Name`; use `HelpText` for the per-row subject |
| A disabled control | Skipped by Tab **and** shows no tooltip, so any explanation attached to it reaches a mouse and nobody else | Leave it enabled, dim it, and let the handler decline |
| An assertion over an empty set | `$locked.Count -eq $eyes.Count` passes trivially when both are 0 | Assert the count is what you expect *first*, then assert the property |

## What this has caught

Recorded so nobody re-derives them. All were invisible to the 271 unit tests.

- **The Navigation screen's keyboard path did not exist.** `NAVIGATION.md` said the chevrons were the
  way to rearrange the pane without a mouse. They live in the editor panel, the panel appears only
  once a screen is selected, and a screen was selected by `PointerPressed` on a `Border` — so the
  entrance to every control on the right-hand side was a mouse click.
- **The account avatar announced itself as `NavigationViewItem`** — its class name. An item drawing
  custom content has no text for UIA to derive a name from.
- **A sentence shown on three rows was true of one of them.** "This is how the pane is managed" fits
  the Navigation screen and not Users or Role permissions.
- **`Load more` closed the row somebody was reading**, because the rebuild discarded expansion state.
- **The pending bar was sliced mid-word.** `TextTrimming` cannot engage inside a horizontal
  `StackPanel`, which hands its children unbounded width.
- **The apply outcome said "moved"** for changes that were renames or visibility toggles.
- **Five icon-only buttons on Notes had tooltips and no names.**
- **"Preview as" was a one-way door.** "Yourself" was the picker's `PlaceholderText`, and a ComboBox
  shows its placeholder only while nothing is selected — so the first role somebody previewed was the
  last, with nothing left in the list to choose to get their own pane back.
- **A `Button` wrapping the row hid the eye inside it.** This one was found by the harness *after* a
  fix, which is the argument for keeping it: the same run that confirmed the row had become reachable
  reported that five visibility controls had gone.

## Extending it

Add checks beside the ones already there; keep them phrased as claims about the product rather than
about the harness (`'Discard drops the staged work'`, not `'invoke returns 0'`). Two helpers:

- `Test-UI <name> { … }` — passes when the command exits 0. For interactions.
- `Assert-That <name> <bool> <detail>` — for anything you computed yourself. The detail string is
  printed on failure and is the difference between a useful run and a rerun.

`Elements -Interactive` returns the flat interactive tree, `AllOf` returns everything including text
runs, `Find1` resolves one selector by name and type, and `Choose` works a ComboBox.

## Known failures

The suite stands at **75 passed, 1 failed** on a clean run. Do not spend time re-diagnosing these:

- **`a heading can be moved down`** — headings offer "move up" only. Every ordering is still
  reachable with repeated "up", so this is an asymmetry rather than a trap, and it is recorded
  rather than fixed.

- **The Users and Role permissions sections can report empty** (`0 rows`, `0 roles`, and the command
  buttons "not found") when the run reaches them straight after the Navigation section's staging,
  dialog and discard sequence. Inspecting those pages by hand immediately afterwards shows every
  control present and correctly named, so this is the harness reading a page that has not settled
  rather than a defect. Re-run those two sections on their own to confirm before believing them.

A second check, `no page has an unnamed interactive control`, failed for a long time over Home,
Users and Role permissions. It passes now, and it is worth keeping green: it is the cheapest guard
against the next icon-only button shipping with a tooltip and no name.
