using System;
using System.IO;
using System.Linq;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Plugins;

/// <summary>
/// Unit tests for <see cref="PluginScaffolder"/>: that what it writes is a package the loader would
/// accept, and that it refuses to write over anything.
/// </summary>
/// <remarks>
/// The manifest it produces is run through the same validator the loader and the packaging tool
/// run, which is the assertion that matters: a project that scaffolds is a project that packs.
/// </remarks>
public sealed class PluginScaffolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PluginScaffolder Scaffolder => new(@"C:\Program Files\App");

    private string Target => Path.Combine(_root, "project");

    /// <summary>Derived, never spelled: the assembly name carries the application's own name.</summary>
    private string ProjectDirectory =>
        Path.Combine(Target, PluginScaffolder.AssemblyNameFor("Weather"));

    private static PluginProjectRequest Request(string directory, bool withPage = true)
    {
        return new PluginProjectRequest("Weather", "Weather", "cloud", directory, withPage);
    }

    [Fact]
    public void Create_WritesAManifestTheValidatorAccepts()
    {
        var written = Scaffolder.Create(Request(Target));

        Assert.True(written.IsSuccess);

        using var stream = File.OpenRead(Path.Combine(Target, "manifest.json"));
        var manifest = PluginManifestJson.Read(stream);

        Assert.NotNull(manifest);
        Assert.Empty(PluginManifestValidator.Validate(manifest));
        Assert.Equal("weather", manifest.Id);
        Assert.Equal("Weather", manifest.ModuleName);
        Assert.Equal(PluginSdkVersion.Current.ToString(), manifest.MinHostSdk);
    }

    /// <summary>
    /// The one property of the generated project the whole mechanism rests on: without it the XAML
    /// builds and then cannot be found at runtime.
    /// </summary>
    [Fact]
    public void Create_TheProjectEmbedsItsXamlInItsOwnResourceIndex()
    {
        _ = Scaffolder.Create(Request(Target));
        var csproj = File.ReadAllText(
            Path.Combine(ProjectDirectory, PluginScaffolder.AssemblyNameFor("Weather") + ".csproj"));

        Assert.Contains("<DisableEmbeddedXbf>false</DisableEmbeddedXbf>", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// A package must not carry the host's own assemblies, so every reference to one is marked not
    /// to be copied.
    /// </summary>
    [Fact]
    public void Create_TheProjectDoesNotCopyTheHostsAssemblies()
    {
        _ = Scaffolder.Create(Request(Target));
        var csproj = File.ReadAllText(
            Path.Combine(ProjectDirectory, PluginScaffolder.AssemblyNameFor("Weather") + ".csproj"));

        var references = csproj.Split("<Reference Include=").Length - 1;

        Assert.Equal(references, csproj.Split("<Private>false</Private>").Length - 1);
    }

    [Fact]
    public void Create_WithoutASamplePage_WritesTheModuleAndNoViews()
    {
        var written = Scaffolder.Create(Request(Target, withPage: false));

        Assert.True(written.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(ProjectDirectory, "Views")));
        Assert.True(File.Exists(Path.Combine(ProjectDirectory, "WeatherModule.cs")));
    }

    [Fact]
    public void Create_LeavesNoPlaceholderBehind()
    {
        var written = Scaffolder.Create(Request(Target));

        Assert.True(written.IsSuccess);

        foreach (var path in written.Value)
        {
            Assert.DoesNotContain("{{", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Create_IntoSomethingThatIsNotEmpty_IsRefused()
    {
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "already-here.txt"), "mine");

        var written = Scaffolder.Create(Request(Target));

        Assert.True(written.IsFailure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("weather")]
    [InlineData("1Weather")]
    [InlineData("Weather Editor")]
    [InlineData("Weather.UI")]
    public void CheckModuleName_RefusesAnythingThatIsNotPascalCase(string moduleName)
    {
        Assert.True(PluginScaffolder.CheckModuleName(moduleName).IsFailure);
    }

    [Theory]
    [InlineData("Weather", "weather")]
    [InlineData("WeatherEditor", "weather-editor")]
    [InlineData("Markdown2Pdf", "markdown2-pdf")]
    public void PluginIDFor_SplitsThePascalCaseIntoTheCatalogIdentity(string moduleName, string expected)
    {
        Assert.Equal(expected, PluginScaffolder.PluginIDFor(moduleName));
    }

    /// <summary>
    /// Every identity the scaffolder derives has to survive the validator, because the name is the
    /// only thing the author typed.
    /// </summary>
    [Theory]
    [InlineData("Weather")]
    [InlineData("WeatherEditor")]
    [InlineData("A")]
    public void CheckModuleName_AcceptsWhatItThenDerivesAValidIdentityFrom(string moduleName)
    {
        Assert.True(PluginScaffolder.CheckModuleName(moduleName).IsSuccess);
    }
}
