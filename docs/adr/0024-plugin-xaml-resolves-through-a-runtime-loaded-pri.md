# 0024 — Plugin XAML resolves through a runtime-loaded PRI and a metadata bridge we own

## Context

WinUI 3 cannot resolve compiled XAML from an assembly outside the application's own directory.
microsoft-ui-xaml#6299 is open; the community plugin sample ships with "plugins cannot use the
resource system" as a known defect. Without an answer to this, a plugin could contribute services
and no screens, which is most of the value gone.

[`DynamicXaml.WinUI`](https://github.com/ahmed605/DynamicXAML) (MIT) solves it, and reading its
source rather than its README settled the question against it. It is a C++/WinRT library that takes
the vtable of a public `ResourceMap`, reads two **hardcoded slot indices**, attaches **Microsoft
Detours** hooks to both process-wide, and decides whether to redirect by sniffing whether the caller
is WinUI. Its `EnableUnsafeHooks` flag is an *additional* hook on top; the Detours path is always
on, and `LoadPri` cannot work without it. So "keep the unsafe hooks off" was never available.

That is native code, a vendored Detours build, vtable slot numbers and return-address sniffing,
inherited for a capability a spike then obtained from public API.

## Decision

**We own the bridge**, in about two hundred lines of C# in `__APP_NAME__.PluginSdk.Xaml`, with no
third-party package and no new pin. It is two independent halves.

**Resources are entirely public API.** The `App` constructor answers
`Application.ResourceManagerRequested` with a `ResourceManager` whose `ResourceNotFound` event
consults one `MainResourceMap` per loaded plugin and calls `SetResolvedCandidate`. A candidate
produced by one `ResourceManager` satisfies a miss raised by another — verified, and it is the fact
the design rests on. The handler reads a list that is filled later, so plugin PRIs may be registered
long after the event has fired.

**XAML types take one reflection call, into our own generated code.** The XAML compiler emits a
lazily-built `List<IXamlMetadataProvider> OtherProviders` into the *host's* assembly, and appending
a plugin's generated provider to it is all a plugin-declared XAML type needs. A partial class part
cannot do it: `XamlTypeInfo.g.cs` is produced by the compiler's second pass, after the C# that would
name those members has already compiled. The bridge finds the plugin's provider **by interface**
rather than by name, because the generated type's namespace is derived from the assembly's own name
and deriving it back would be a second place to get that spelling right.

Attach must happen in the `App` constructor, before `InitializeComponent`: the event is raised once
per WinUI thread during initialisation and cannot be answered any later.

## Consequences

Compiled XAML, nested XBF, `x:Bind` with code-behind, `{ThemeResource}`, host `{StaticResource}`
styles and plugin-declared XAML types all work from an assembly loaded outside the application
directory. Every failure degrades to "this plugin did not load, here is why" on its row, which the
Detours approach cannot offer.

**Plugin `.resw` localisation does not resolve, and this is the one real gap.** `ResourceNotFound`
fires on a *value* miss and is silent on a *subtree* miss, and `x:Uid` walks subtrees. That single
asymmetry is why XBF succeeds and `x:Uid` fails. DynamicXaml closes it only with the `TryGetSubtree`
hook we are declining. The supported answer is that a plugin reads its own strings in code, and the
packaging tool refuses `x:Uid` in plugin markup so an author finds out at pack time rather than as a
blank label in front of a user.

The bridge depends on three private members the XAML compiler generates into our own DLL. If a
Windows App SDK release renames one, `AddMetadataProvider` returns a failure naming the member it
could not find: plugins with their own XAML types stop loading and say so, and everything else keeps
working. Pinning the Windows App SDK is already policy, and the Plugins screen is where the failure
becomes visible.

Two plugins declaring the same XAML type name is decided by list order, first constructible wins.
Packaged (MSIX) mode is untested and out of scope for v1.
