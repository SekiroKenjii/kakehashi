# 0023 — Plugins load during host construction, and the disk settles before they do

## Context

The client already let a user turn a module off and on while it ran, but every module was compiled
in and every service was registered at startup. A plugin is an assembly set built somewhere else,
and the question is when it may join.

`IModule.RegisterServices` takes an `IServiceCollection`. That collection stops accepting
registrations at `builder.Build()`, which happens once, inside `AppHost.Build()`, before any window
exists. So a plugin whose services are to be resolvable at all has to be loaded before that call —
there is no later moment, and adding one would mean a second container or a service locator.

Removal has the mirror problem. A loaded assembly cannot be unloaded from the default load context,
and its files stay open for as long as the process does.

## Decision

**Plugins load inside `AppHost.Build()`, before `AddModules`.** Two consequences follow, and they
are the shape of the feature rather than defects in it:

- installing takes effect at the **next** launch;
- removing happens at the **start** of one.

Each launch settles the disk before it opens a single assembly, in this order: delete what an
uninstall marked for removal, promote what an install staged over what is installed, then load what
is installed. Step two is a delete-then-move, so a crash between the two leaves the staged copy
where the next launch finds it and repeats the promotion.

**`Assembly.LoadFrom` into the default load context**, not a custom `AssemblyLoadContext`. A custom
context is what microsoft-ui-xaml#3888 reports breaking XAML user controls; removal already needs a
restart, so isolation would buy nothing but a second way for type identity to go wrong.

**A refusal is data, not an exception.** A plugin that will not load becomes a row with a reason on
the Plugins screen; an unreadable state file is set aside rather than written over. Every call into
plugin-authored code is made inside a filter, so a plugin that throws is a row too. The loader runs
before the container exists, so it cannot log — faults are data on `PluginCatalog`, which the
Plugins screen reads.

**And a plugin is asked once.** Its name, its descriptor and its navigation items are kept, and
every later reader is given those answers rather than the plugin — so it cannot say one thing to be
checked and another to be used.

## Consequences

"Restart required" on the Plugins screen is the literal truth rather than a caution, and the banner
says which change is waiting.

A plugin joins `IModuleRegistry.All`, so enabling and disabling one needs no new machinery — it is
the same attach/detach a compiled-in module already has, and it is instant because nothing is
loaded or unloaded by it.

`PublishTrimmed` is `false` and stated so in the project file. The plugin assemblies do not exist at
publish time, so no annotation can make this path trim-safe; turning trimming on later would break
every plugin at runtime rather than at build.

`Plugins:Enabled` in configuration is the kill switch, defaulting to `true`. Zero plugins is a
no-op, so the default costs nothing and a fresh scaffold shows the feature exists. Off, it loads
nothing and the screen says so, because installing still works and a package staged against a switch
that is off waits for a launch that will not load it. MSIX is untested rather than refused: `%LOCALAPPDATA%` and `ms-appx` both change meaning
there, and v1 supports unpackaged only.

A plugin cannot substitute a host service. `AddModules` snapshots the collection, calls
`RegisterServices`, and rolls the whole registration back if anything the host registered went
missing — which is stronger than any static check of the plugin's code, because it observes the
result rather than the source.

Nor can it register over one. Removal is not required to substitute a service — the container
resolves the last descriptor and a plugin registers last — so a registration naming a service type
the host already provides is refused by name.

The state file is written after the promotion rather than before, which would leave a plugin's files
present under a version its record does not name. Rather than order the two writes, the next launch
reads the disk: a staged directory that is gone and an installed one that is there is a promotion
that happened, and it is adopted. Recovery from what is on disk is cheaper to keep right than an
ordering nothing enforces.
