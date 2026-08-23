# Plugins

A module built somewhere else, packaged as a file, and made part of a running installation.

The application already let a user turn feature modules on and off. This is the other half: a module
that was never compiled in — an assembly set named `__APP_NAME__.Modules.<Name>.*`, downloaded from
this deployment's catalog or handed over as a file — becoming a first-class part of the composition,
with its own screens.

## What a plugin can do, and what it cannot

A plugin **runs in this process with everything this process can do.** There is no sandbox. It can
read local files, reach the session token, and call the backend as the signed-in user. Every
protection described below is about deciding whether to run it, because after that decision there is
nothing left to enforce.

| It can | It cannot |
| --- | --- |
| register services into the host's container | remove or register over a service the host registered |
| add screens to the navigation pane | replace a screen this build already answers to |
| ship compiled XAML, `x:Bind`, code-behind and its own XAML types | resolve its own `.resw` strings through `x:Uid` |
| use host `{StaticResource}` styles and `{ThemeResource}` | be unloaded without restarting |
| be turned off and on instantly, like any module | have its screens rearranged from the Navigation screen |

The refusals are not conventions, and there are three ways a registration could stop being additive.
Removing an entry is caught against a snapshot. Registering a service type the host already provides
is caught by name — removal is not required to substitute one, because the container resolves the
last descriptor and a plugin registers last. Throwing is caught by a filter. Any of the three rolls
the whole registration back and becomes a row.

A page whose key collides with one this build already answers to is a load failure with the key
named on the row. The reserved keys come from the compiled-in modules' navigation items, the host's
pane entries, and the two screens the shell registers without a pane item of their own — Home and
Settings. The key is matched the way the navigation service matches it, because one derived by a
different rule would not be the one that collides.

## The lifecycle, and why a restart

`IModule.RegisterServices` takes an `IServiceCollection`, and that collection stops accepting
registrations at `builder.Build()` — once, inside host construction, before any window exists. So a
plugin loads there or its services are never resolvable. Removal has the mirror problem: a loaded
assembly cannot be unloaded, and its files stay open for the life of the process.

```text
%LOCALAPPDATA%\__APP_NAME__\Plugins\
  installed\<id>\<version>\     what this launch loaded
  staged\<id>\<version>\        what the next launch will load
  state.json                    versions, source, digest, signer, consent
```

Each launch settles the disk before it opens a single assembly:

1. delete every directory an uninstall marked, and clear its entry;
2. promote `staged\<id>\<v>` over `installed\<id>\*`, delete-then-move;
3. load what is in `installed\`.

A crash between the delete and the move leaves the staged copy where the next launch finds it, and
step 2 repeats. So **installing takes effect at the next launch, and removing happens at the start
of one**, and the screen's "Restart required" banner is the literal truth rather than a caution.

A crash between the move and the state file being written is covered from the other side: the next
launch finds the staged directory gone and the new one present, and adopts what is on disk rather
than faulting on both halves. An uninstall whose files are still held open keeps its record, so the
launch after that tries again instead of orphaning the directory.

Turning a loaded plugin off and on is instant: it is the same attach/detach a compiled-in module
has, and nothing is loaded or unloaded by it.

Nothing here throws. A plugin that will not load is a row with a reason, an unreadable state file
means no plugins rather than no application, and every call into plugin-authored code — its module
constructor, its `GetNavigationItems()`, its `RegisterServices`, and the constructor of its
generated XAML metadata provider — is made inside a filter that turns an exception into that row.

`Plugins:Enabled: false` in `appsettings.json` is the switch for when even that is not enough. The
screen says so rather than showing an empty list: installing still works and nothing will load what
it stages until the switch goes back.

## Trust

Two levels, and the difference is one comparison.

**Verified** — the entry assembly's Authenticode signature is valid *and* its signer's full
distinguished name matches, exactly, the publisher of the running executable. Installs without a
prompt. It vouches for that one file: the other assemblies in `lib/` are extracted and loaded
without a signature check of their own.

**Unofficial** — everything else, including a perfectly valid signature from somebody else. Always
prompts.

The signature is checked with `WinVerifyTrust` rather than by building the certificate chain,
because those answer different questions. A chain build says the signer is who they claim to be; it
does not say the bytes are the ones that were signed. A copy of a Microsoft-signed system binary
with one byte changed still names Microsoft as its signer, and only the trust provider calls it
tampered.

**A build this machine cannot verify vouches for nothing.** The publisher comes from the running
executable, and anything short of a valid signature there — unsigned, expired, tampered — makes
every package Unofficial and asks about all of them. That is the correct behaviour, not a degraded
one.

**Unofficial is not one thing, and the screen says which.** The verdict collapses eight statuses
into two, so the status is kept beside it: unsigned, signed by another publisher, and *modified
since it was signed* read differently in the prompt and on the row, because the last of those is the
one that is not merely unvouched-for.

Revocation is checked, and only against what this machine has already cached
(`WTD_CACHE_ONLY_URL_RETRIEVAL`). A certificate this machine knows was revoked reads as revoked; one
it has never seen a list for reads as valid, because not knowing is not the same as knowing, and an
install that reached for a CRL would be one that needed the network and stalled without it.

**Consent is keyed on the identity and the digest together.** Different bytes are a different
package whatever its version says, so an update is a fresh decision and the prompt returns — while a
reinstall of exactly what was already agreed to does not ask again. A prompt that appears when
nothing has changed is one people learn to click through.

A catalog download is checked against the digest the catalog published, before anything opens the
archive, and bytes that do not match are deleted rather than inspected. That digest is the server's
own — it hashes what arrived at publish and refuses a mismatch — so the value being compared is one
the server produced rather than one an uploader asserted.

## What the manifest says, and what it does not

`manifest.json` is the only thing read before any of the package's code runs, which is why it
repeats what the module also declares in code.

```jsonc
{
  "schemaVersion": 1,
  "id": "markdown-editor",
  "moduleName": "MarkdownEditor",
  "displayName": "Markdown Editor",
  "version": "0.9.1",
  "entryAssembly": "__APP_NAME__.Modules.MarkdownEditor.UI.dll",
  "moduleType": "__ROOT_NAMESPACE__.Modules.MarkdownEditor.UI.MarkdownEditorModule",
  "priFiles": ["__APP_NAME__.Modules.MarkdownEditor.UI.pri"],
  "minHostSdk": "1.1",
  "navigation": [{ "title": "Markdown", "group": "Utilities", "page": "MarkdownPage" }],
  "callsPermission": "markdown.access"
}
```

`navigation` exists so the install prompt can say *"adds a 'Markdown' screen under Utilities"*
**without executing the plugin**. The runtime truth still comes from `GetNavigationItems()`.

`callsPermission` is shown in the install prompt as **disclosure, never a gate** — see
[ADR 0025](adr/0025-a-plugins-declared-permission-is-disclosure.md). Nothing stored client-side may
be read as authorization; the server refuses what a plugin is not entitled to ask for, at the one
place that sees every request. A plugin's navigation items carry no `RequiredPermission` at all,
because a key a plugin invented is not in the server's catalogue and setting it would make the
plugin hide its own screen.

`minHostSdk` is checked **before** `Assembly.LoadFrom`, so a package built against a later host is a
refused install rather than a `MissingMethodException` mid-navigation.

## Writing one

**Plugins → Develop** scaffolds a project that builds and loads without being edited first. It
references the assemblies sitting beside the running executable — `SharedKernel` and `UI.Contracts`,
both with `Private=false` — so it compiles against exactly the ones it will be loaded next to.

`<DisableEmbeddedXbf>false</DisableEmbeddedXbf>` in the generated project file is the one property
the whole mechanism rests on: it is what puts the compiled XAML in the assembly's own resource
index, which is the file the host loads. Without it the pages build and then cannot be found.

Then, from the Develop tab or from a build server:

```sh
dotnet run --project client/tools/__APP_NAME__.PluginTool -- validate <project>
dotnet run --project client/tools/__APP_NAME__.PluginTool -- pack <project>
```

The tool is `PackAsTool`, and nothing here publishes it: a deployment that wants
`__APP_NAME_LOWER__-plugin` on a build server packs it and installs from that package. CI packs it
on every run, so that it stays installable is a thing the gates know rather than a thing somebody
finds out.

`validate` packs the project in memory through the same code `pack` writes with, opens the result
and checks *that* — so what an author is told is what a user's installation would say, and there is
one packaging path rather than two that can drift. It reports a `moduleType` the entry assembly does
not declare, one that does not implement `IModule`, an assembly carrying compiled XAML whose
resource index is not declared, a screen the manifest promises that the package cannot show, and
`x:Uid` in markup.

It reads metadata and never executes the module, so two limits are worth knowing: it cannot confirm
that `GetNavigationItems()` agrees with the manifest, and it sees only pages that derive directly
from `Page`. The loader still refuses the rest at load.

## The gap worth knowing about

**A plugin's own `.resw` strings do not resolve through `x:Uid`.** The host answers a failed
resource lookup by consulting each plugin's resource index, and that event fires on a *value* miss
and is silent on a *subtree* miss — which is what `x:Uid` walks. Compiled XAML works because it is a
value lookup; a localized label is not. `validate` refuses `x:Uid` in plugin markup so an author
finds out at pack time rather than as a blank label in front of a user. Read your own strings in
code. [ADR 0024](adr/0024-plugin-xaml-resolves-through-a-runtime-loaded-pri.md) has the whole mechanism.

Two more, stated rather than discovered:

- **Trimming is off and has to stay off.** `PublishTrimmed` is `false` with a comment naming the
  loader. Plugin assemblies do not exist at publish time, so no annotation can make this path
  trim-safe; turning trimming on would break every plugin at runtime rather than at build.
- **MSIX is out of scope for v1.** Packaged, `%LOCALAPPDATA%` and `ms-appx` both change meaning.
  Nothing checks for it: the app defaults to unpackaged, and `-p:Packaged=true` is untested against
  this feature rather than refused by it.

## Verifying it end to end

The unit tests cover the packaging library and the view models; the XAML bridge's working path needs
a live `Application`, so it is verified by running the thing:

1. Plugins → Develop → scaffold a module into an empty folder.
2. Build it, then **Check** and **Pack** from the same tab.
3. Plugins → Installed → **Install from file**. Confirm the prompt shows Unofficial, the four
   warnings, the SHA-256, and that the install button stays unavailable until the box is ticked.
4. Restart. The new entry appears in the pane and its page opens — this is the step that proves the
   XAML and resource-index path.
5. Toggle it off: the pane item disappears without a restart, and the state survives a relaunch.
6. Corrupt one byte of a package *from the catalog* and reinstall: refused on the digest. A file
   install has no published digest to compare against — its digest is computed and recorded, and a
   corrupt archive surfaces as a failure to open rather than as a mismatch.
7. Uninstall, restart, confirm the directory under `installed\` is gone.

Step 4 is the one to run first after any Windows App SDK upgrade. The automated pass in
`.claude/skills/ui-testing` visits the screen and checks its tabs, its footer disclaimer and its
command buttons — but installs nothing: a binary fixture does not belong in a template, and building
one inside a UI test would make it depend on a toolchain and a host build path. Everything from
`Assembly.LoadFrom` onward is covered by this list and by nothing else.

## Where the code is

| | |
| --- | --- |
| `client/src/Shared/__APP_NAME__.PluginSdk.Abstractions/` | the manifest, the package format, the validators, the packager |
| `client/src/Shared/__APP_NAME__.PluginSdk.Xaml/` | the host's half of the XAML bridge |
| `client/src/App/__APP_NAME__.App/Plugins/` | paths, state, trust, the loader, the installer, the scaffolder |
| `client/src/App/__APP_NAME__.App/UI/PluginsPage.*` | the screen: Installed, Browse catalog, Develop |
| `client/tools/__APP_NAME__.PluginTool/` | `validate` and `pack` |
| `server/internal/modules/plugins/` | the catalog, the artifacts, publishing |

ADRs [0023](adr/0023-plugins-load-at-startup.md),
[0024](adr/0024-plugin-xaml-resolves-through-a-runtime-loaded-pri.md),
[0025](adr/0025-a-plugins-declared-permission-is-disclosure.md),
[0026](adr/0026-plugin-artifacts-stream-over-the-contract.md) and
[0027](adr/0027-an-install-is-reported-through-the-module-that-published-it.md) record why each of
these is shaped the way it is.
