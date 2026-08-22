using System.Collections.Generic;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// What a plugin package declares about itself, and the only thing read before any of its code
/// runs.
/// </summary>
/// <remarks>
/// It repeats what the module also declares in code — the module name, the screens — because the
/// install dialog has to tell the user what they are agreeing to before there is anything to ask.
/// The runtime truth still comes from <c>IModule</c>; the packaging tool asserts the two agree.
/// </remarks>
public sealed record PluginManifest
{
    /// <summary>The manifest format this package was written for.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Catalog identity: lower case, digits and single hyphens.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Must equal the module's <c>IModule.Name</c>, which keys attach and detach.</summary>
    public string ModuleName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>The package's own version, <c>major.minor.patch</c>.</summary>
    public string Version { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Homepage { get; init; } = string.Empty;

    /// <summary>The file under <c>lib/</c> that holds <see cref="ModuleType"/>.</summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>The assembly-qualified-free full name of the <c>IModule</c> implementation.</summary>
    public string ModuleType { get; init; } = string.Empty;

    /// <summary>
    /// The resource indexes to load before the entry assembly, one per XAML-bearing assembly.
    /// </summary>
    /// <remarks>
    /// A plugin's compiled XAML lives in its own PRI rather than the host's, which is what makes it
    /// resolvable at all: docs/adr/0024-plugin-xaml-resolves-through-a-runtime-loaded-pri.md
    /// </remarks>
    public IReadOnlyList<string> PriFiles { get; init; } = [];

    /// <summary>The oldest host SDK this package runs on, <c>major.minor</c>.</summary>
    public string MinHostSdk { get; init; } = string.Empty;

    /// <summary>The screens this package adds, for disclosure before installing.</summary>
    public IReadOnlyList<PluginNavigationEntry> Navigation { get; init; } = [];

    /// <summary>
    /// The server permission this plugin's endpoints are gated on, if it has a server half.
    /// </summary>
    /// <remarks>
    /// Disclosure, never a gate. Nothing a package declares is read as authorization:
    /// docs/adr/0015-module-attachment-is-not-a-security-boundary.md
    /// </remarks>
    public string CallsPermission { get; init; } = string.Empty;

    /// <summary>
    /// Set when the package carries a XAML-bearing library of its own that resolves its resources
    /// through unprefixed <c>ms-appx</c> URIs, which only the host's unsafe hooks can redirect.
    /// </summary>
    public bool RequiresUnsafeXamlHooks { get; init; }
}

/// <summary>One screen a package adds to the navigation pane.</summary>
public sealed record PluginNavigationEntry
{
    public string Title { get; init; } = string.Empty;

    /// <summary>A name from the client's icon vocabulary, never a glyph.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>The pane heading this screen sits under.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>The page type's name, which the navigation service requires to end in "Page".</summary>
    public string Page { get; init; } = string.Empty;
}
