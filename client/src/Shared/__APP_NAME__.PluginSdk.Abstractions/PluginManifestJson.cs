using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// Reads and writes <c>manifest.json</c>. One shape, so the packaging tool and the host cannot
/// disagree about what a package said.
/// </summary>
public static class PluginManifestJson
{
    /// <summary>Returns null when the stream does not hold a JSON object.</summary>
    public static PluginManifest? Read(Stream stream)
    {
        var manifest = JsonSerializer.Deserialize(stream, PluginManifestJsonContext.Default.PluginManifest);

        return manifest is null ? null : Normalize(manifest);
    }

    public static void Write(Stream stream, PluginManifest manifest)
    {
        JsonSerializer.Serialize(stream, manifest, PluginManifestJsonContext.Default.PluginManifest);
    }

    /// <summary>
    /// Replaces the nulls deserialization leaves behind, so every field a caller reads is the type
    /// it is declared as.
    /// </summary>
    /// <remarks>
    /// The JSON source generator assigns every mapped property, including the ones the document
    /// omits, which discards the initializers an <c>init</c> property carries — the reflection-based
    /// serializer keeps them and a <c>set</c> property keeps them, so the behaviour is specific to
    /// this combination. Without this, a manifest missing <c>displayName</c> reaches the validator
    /// as null and the length check throws.
    /// </remarks>
    private static PluginManifest Normalize(PluginManifest manifest)
    {
        return manifest with {
            Id = OrEmpty(manifest.Id),
            ModuleName = OrEmpty(manifest.ModuleName),
            DisplayName = OrEmpty(manifest.DisplayName),
            Description = OrEmpty(manifest.Description),
            Version = OrEmpty(manifest.Version),
            Author = OrEmpty(manifest.Author),
            Homepage = OrEmpty(manifest.Homepage),
            EntryAssembly = OrEmpty(manifest.EntryAssembly),
            ModuleType = OrEmpty(manifest.ModuleType),
            PriFiles = OrEmpty(manifest.PriFiles),
            MinHostSdk = OrEmpty(manifest.MinHostSdk),
            Navigation = [.. NormalizeNavigation(manifest.Navigation)],
            CallsPermission = OrEmpty(manifest.CallsPermission),
        };
    }

    private static IEnumerable<PluginNavigationEntry> NormalizeNavigation(
        IReadOnlyList<PluginNavigationEntry>? entries)
    {
        foreach (var entry in OrEmpty(entries))
        {
            yield return entry is null
                ? new PluginNavigationEntry()
                : entry with {
                    Title = OrEmpty(entry.Title),
                    Icon = OrEmpty(entry.Icon),
                    Group = OrEmpty(entry.Group),
                    Page = OrEmpty(entry.Page),
                };
        }
    }

    private static string OrEmpty(string? value)
    {
        return value ?? string.Empty;
    }

    private static IReadOnlyList<T> OrEmpty<T>(IReadOnlyList<T>? value)
    {
        return value ?? [];
    }
}

/// <summary>
/// Comments and trailing commas are tolerated because a manifest is hand-edited during
/// development; everything written back out is plain JSON.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(PluginManifest))]
internal sealed partial class PluginManifestJsonContext : JsonSerializerContext
{
}
