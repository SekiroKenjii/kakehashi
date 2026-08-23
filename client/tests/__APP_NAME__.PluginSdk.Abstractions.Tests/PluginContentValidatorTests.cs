using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;
using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginContentValidator"/>, over packages holding this test assembly.
/// </summary>
/// <remarks>
/// A real assembly, because every rule here is a question about metadata and a fabricated file
/// would only prove the reader agrees with the fabrication. The types it finds are in Fixtures/.
/// <para>
/// Assertions are by error code rather than by an empty list: the fixture assembly deliberately
/// holds a page named wrongly, so that one refusal is present in every case.
/// </para>
/// </remarks>
public sealed class PluginContentValidatorTests : IDisposable
{
    private const string _entryAssembly = "App.Modules.Weather.UI.dll";
    private const string _fixtures = "__ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests.Fixtures.";

    private readonly List<PluginPackage> _opened = [];
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        foreach (var package in _opened)
        {
            package.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Manifest(
        string moduleType = _fixtures + "WeatherModule",
        string priFiles = """["App.Modules.Weather.UI.pri"]""",
        string navigation = "[]")
    {
        return $$"""
            {
              "schemaVersion": 1,
              "id": "weather",
              "moduleName": "Weather",
              "displayName": "Weather",
              "version": "1.0.0",
              "entryAssembly": "{{_entryAssembly}}",
              "moduleType": "{{moduleType}}",
              "priFiles": {{priFiles}},
              "navigation": {{navigation}},
              "minHostSdk": "1.0"
            }
            """;
    }

    /// <summary>A package carrying this test assembly under the name the manifest declares.</summary>
    private PluginPackage Package(string manifest)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry(PluginPackage.ManifestEntryName);

            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifest);
            }
            archive.CreateEntryFromFile(
                typeof(PluginContentValidatorTests).Assembly.Location,
                PluginPackage.LibraryFolder + _entryAssembly);

            var indexEntry = archive.CreateEntry(PluginPackage.LibraryFolder + "App.Modules.Weather.UI.pri");

            using (var index = new StreamWriter(indexEntry.Open()))
            {
                index.Write("index");
            }

            // What a real build drags in beside the plugin: the WindowsAppSDK brings native
            // libraries along, and nothing about them is this validator's business.
            var native = archive.CreateEntry(PluginPackage.LibraryFolder + "Native.dll");

            using var bytes = native.Open();
            bytes.Write([0x4D, 0x5A, 0x90, 0x00]);
        }
        stream.Position = 0;
        var opened = PluginPackage.Open(stream, leaveOpen: false);

        Assert.True(opened.IsSuccess);
        _opened.Add(opened.Value);

        return opened.Value;
    }

    private static bool Has(IReadOnlyList<Error> errors, string code)
    {
        return errors.Any(error => error.Code == code);
    }

    [Fact]
    public void Validate_AModuleTypeTheAssemblyDoesNotDeclareIsATypo()
    {
        var errors = PluginContentValidator.Validate(Package(Manifest(_fixtures + "WeatherModul")));

        Assert.True(Has(errors, "Plugin.Content.ModuleTypeMissing"));
        Assert.False(Has(errors, "Plugin.Content.ModuleTypeNotAModule"));
    }

    /// <summary>A different mistake with a different fix: the type is there, the interface is not.</summary>
    [Fact]
    public void Validate_ATypeThatIsNotAModuleIsNamedAsSuch()
    {
        var errors = PluginContentValidator.Validate(Package(Manifest(_fixtures + "WeatherSettings")));

        Assert.True(Has(errors, "Plugin.Content.ModuleTypeNotAModule"));
        Assert.False(Has(errors, "Plugin.Content.ModuleTypeMissing"));
    }

    [Fact]
    public void Validate_AModuleThatIsThereIsNotComplainedAbout()
    {
        var errors = PluginContentValidator.Validate(Package(Manifest()));

        Assert.False(Has(errors, "Plugin.Content.ModuleTypeMissing"));
        Assert.False(Has(errors, "Plugin.Content.ModuleTypeNotAModule"));
    }

    /// <summary>
    /// The silent one: the pages build, and then nothing can find them, because the index holding
    /// their compiled markup was never named.
    /// </summary>
    [Fact]
    public void Validate_AnAssemblyWithCompiledXamlNeedsItsIndexDeclared()
    {
        var errors = PluginContentValidator.Validate(Package(Manifest(priFiles: "[]")));

        Assert.True(Has(errors, "Plugin.Content.ResourceIndexUndeclared"));
    }

    [Fact]
    public void Validate_ADeclaredIndexSatisfiesIt()
    {
        var errors = PluginContentValidator.Validate(Package(Manifest()));

        Assert.False(Has(errors, "Plugin.Content.ResourceIndexUndeclared"));
    }

    [Fact]
    public void Validate_APageWhoseKeyWouldBeEmptyIsRefused()
    {
        var errors = PluginContentValidator.Validate(Package(Manifest()));

        Assert.True(Has(errors, "Plugin.Manifest.PageNameInvalid"));
    }

    [Fact]
    public void Validate_AScreenTheManifestPromisesMustBeInThePackage()
    {
        var navigation = """[{"title":"Weather","group":"Utilities","page":"MissingPage"}]""";

        var errors = PluginContentValidator.Validate(Package(Manifest(navigation: navigation)));

        Assert.True(Has(errors, "Plugin.Content.NavigationPageMissing"));
    }

    /// <summary>
    /// One direction only: a page reached from inside the plugin's own screens is not a mistake.
    /// </summary>
    [Fact]
    public void Validate_APageTheManifestDoesNotMentionIsNotAProblem()
    {
        var navigation = """[{"title":"Weather","group":"Utilities","page":"WeatherPage"}]""";

        var errors = PluginContentValidator.Validate(Package(Manifest(navigation: navigation)));

        Assert.False(Has(errors, "Plugin.Content.NavigationPageMissing"));
    }

    /// <summary>
    /// A build brings along more than an author chose — native libraries included — so only the
    /// assembly the manifest points at has to be one the reader can read.
    /// </summary>
    [Fact]
    public void Validate_ANativeLibraryBesideTheEntryAssemblyIsNotAProblem()
    {
        var package = Package(Manifest());
        var errors = PluginContentValidator.Validate(package);

        Assert.Contains("Native.dll", package.LibraryFiles);
        Assert.False(Has(errors, "Plugin.Content.EntryAssemblyUnreadable"));
    }

    [Fact]
    public void ValidateMarkup_FindsALocalizedLabelThatWouldRenderEmpty()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "WeatherPage.xaml"),
            """<Page><TextBlock x:Uid="Title" /></Page>""");

        var errors = PluginContentValidator.ValidateMarkup(_root);

        Assert.True(Has(errors, "Plugin.Content.LocalizedMarkup"));
    }

    [Fact]
    public void ValidateMarkup_PlainMarkupPasses()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "WeatherPage.xaml"),
            """<Page><TextBlock Text="Title" /></Page>""");

        Assert.Empty(PluginContentValidator.ValidateMarkup(_root));
    }
}
