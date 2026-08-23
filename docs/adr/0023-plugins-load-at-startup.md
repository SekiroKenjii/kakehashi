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
the Plugins screen; an unreadable state file means no plugins rather than no application. The loader
runs before the container exists, so it cannot log — faults are data on `PluginCatalog`, which the
Plugins screen reads.

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
nothing and says nothing — the screen still offers to install, which is a rough edge rather than a
decision. MSIX is untested rather than refused: `%LOCALAPPDATA%` and `ms-appx` both change meaning
there, and v1 supports unpackaged only.

A plugin cannot substitute a host service. `AddModules` snapshots the collection, calls
`RegisterServices`, and rolls the whole registration back if anything the host registered went
missing — which is stronger than any static check of the plugin's code, because it observes the
result rather than the source.

Two things this decision does not yet buy, and both are stated in `docs/PLUGINS.md` rather than
implied away. Calls into plugin-authored code are guarded for the module's constructor and not for
`GetNavigationItems`, `RegisterServices` or the generated XAML provider's constructor, so a plugin
that throws from one of those still reaches the exception window. And the state file is written
after the promotion rather than before, which leaves one crash window in which a plugin's files are
present under a version its record does not name. Neither is inherent to loading at startup; both
are work the shape above makes possible rather than prevents.
