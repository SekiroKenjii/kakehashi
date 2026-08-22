using System.Collections.Generic;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// A manifest every field of which is valid, so a test that changes one field is testing that
/// field.
/// </summary>
internal static class PluginManifests
{
    public const string EntryAssembly = "SmokeApp.Modules.Weather.UI.dll";

    public const string PriFile = "SmokeApp.Modules.Weather.UI.pri";

    public static PluginManifest Valid()
    {
        return new PluginManifest {
            SchemaVersion = PluginManifestValidator.SupportedSchemaVersion,
            Id = "weather",
            ModuleName = "Weather",
            DisplayName = "Weather",
            Description = "Local weather dashboard with forecast tiles.",
            Version = "0.9.1",
            Author = "npham-dev",
            Homepage = "https://example.com/weather",
            EntryAssembly = EntryAssembly,
            ModuleType = "SmokeApp.Modules.Weather.UI.WeatherModule",
            PriFiles = new List<string> { PriFile },
            MinHostSdk = "0.1",
            Navigation = new List<PluginNavigationEntry> {
                new() { Title = "Weather", Icon = "cloud", Group = "Utilities", Page = "WeatherPage" },
            },
            CallsPermission = "weather.access",
        };
    }
}
