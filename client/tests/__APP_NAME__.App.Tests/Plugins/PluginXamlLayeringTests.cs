using System;
using System.IO;
using System.Linq;
using System.Reflection;
using __ROOT_NAMESPACE__.PluginSdk.Xaml;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Plugins;

/// <summary>
/// Holds the XAML bridge to the one project reference it needs, and covers the refusals a caller
/// depends on.
/// </summary>
/// <remarks>
/// It lives here rather than in the architecture suite because that project targets a framework
/// without WinUI, and the bridge cannot be reflected on from there at all.
/// <para>
/// What none of this can check is a plugin, which is not in this solution and never will be. That
/// gap is why the packaging tool validates a package and why the loader re-runs the cheap half of
/// it; the resolving path itself is exercised against a fixture plugin in the running application.
/// </para>
/// </remarks>
public sealed class PluginXamlLayeringTests
{
    private static readonly Assembly _bridge = typeof(PluginXamlHost).Assembly;

    [Fact]
    public void Bridge_ReferencesOnlyTheSharedKernel()
    {
        var references = _bridge
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("__APP_NAME__.", StringComparison.Ordinal));

        Assert.Equal(["__APP_NAME__.SharedKernel"], references);
    }

    [Fact]
    public void Bridge_DoesNotReachIntoTheHost()
    {
        var references = _bridge
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain("__APP_NAME__.App", references);
        Assert.DoesNotContain("__APP_NAME__.UI.Contracts", references);
    }

    [Fact]
    public void AddMetadataProvider_BeforeAttach_FailsInsteadOfThrowing()
    {
        var host = new PluginXamlHost();

        var result = host.AddMetadataProvider(typeof(PluginXamlLayeringTests).Assembly);

        Assert.True(result.IsFailure);
        Assert.Equal("PluginXaml.NotAttached", result.Error.Code);
    }

    [Fact]
    public void AddPackage_IndexThatIsNotThere_Fails()
    {
        var host = new PluginXamlHost();

        var result = host.AddPackage(Path.Combine(Path.GetTempPath(), "no-such-plugin.pri"));

        Assert.True(result.IsFailure);
        Assert.Equal("PluginXaml.ResourceIndexMissing", result.Error.Code);
    }

    [Fact]
    public void AddPackage_FileThatIsNotAnIndex_Fails()
    {
        var host = new PluginXamlHost();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "not a resource index");

        try
        {
            var result = host.AddPackage(path);

            Assert.True(result.IsFailure);
            Assert.Equal("PluginXaml.ResourceIndexUnreadable", result.Error.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
