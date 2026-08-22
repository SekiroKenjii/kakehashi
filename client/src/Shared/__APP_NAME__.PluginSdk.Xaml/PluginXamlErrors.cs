using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Xaml;

/// <summary>Why a plugin's XAML could not be made resolvable.</summary>
public static class PluginXamlErrors
{
    public static readonly Error NotAttached = new(
        "PluginXaml.NotAttached", "The plugin XAML host was used before it was attached to the application.");

    public static Error ResourceIndexMissing(string path)
    {
        return new Error("PluginXaml.ResourceIndexMissing", $"There is no resource index at '{path}'.");
    }

    public static Error ResourceIndexUnreadable(string path)
    {
        return new Error("PluginXaml.ResourceIndexUnreadable", $"'{path}' is not a readable resource index.");
    }

    /// <summary>
    /// An index that resolves nothing. Its own reason rather than a missing file, because the
    /// resource manager reports a path that does not exist as an index with nothing in it, and a
    /// plugin whose compiled XAML is absent has to fail here rather than at the first navigation.
    /// </summary>
    public static Error ResourceIndexEmpty(string path)
    {
        return new Error("PluginXaml.ResourceIndexEmpty", $"The resource index at '{path}' indexes nothing.");
    }

    /// <summary>
    /// The application's own generated metadata provider was not shaped the way this build expects.
    /// </summary>
    /// <remarks>
    /// Reported rather than thrown: only a plugin that declares its own XAML types needs the
    /// provider, so everything else keeps working and one plugin's row carries the reason.
    /// </remarks>
    public static Error MetadataBridgeUnavailable(string member)
    {
        return new Error(
            "PluginXaml.MetadataBridgeUnavailable",
            $"The application's XAML metadata provider has no '{member}'. Plugin-declared XAML types "
                + "cannot be resolved in this build.");
    }

    public static Error MetadataProviderUnusable(string assemblyName, string reason)
    {
        return new Error(
            "PluginXaml.MetadataProviderUnusable",
            $"The XAML metadata provider in '{assemblyName}' could not be used: {reason}");
    }
}
