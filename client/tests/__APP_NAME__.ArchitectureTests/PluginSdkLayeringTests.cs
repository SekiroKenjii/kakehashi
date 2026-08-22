using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using Xunit;

namespace __ROOT_NAMESPACE__.ArchitectureTests;

/// <summary>
/// Holds the plugin SDK's abstractions to what a plugin author's non-UI code may compile against.
/// </summary>
/// <remarks>
/// This is the half a packaging tool and a plugin's application layer both reference, so anything
/// it drags in becomes a dependency of every plugin project. It is also the half that has to be
/// readable without WinUI: a tool that validates a package runs on a build server, not on a
/// desktop.
/// <para>
/// These checks cannot see a plugin's own references — a plugin is not in this solution, and no
/// reflection-over-assemblies test can constrain an assembly it never sees. That gap is why the
/// packaging tool validates a package and why the host re-runs the cheap half of it at load.
/// </para>
/// </remarks>
public sealed class PluginSdkLayeringTests
{
    private static readonly Assembly _abstractions = typeof(PluginManifest).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void Abstractions_ReferenceOnlyTheSharedKernel()
    {
        var references = ReferencedAssemblyNames(_abstractions)
            .Where(name => name.StartsWith(TestConstants.AssemblyPrefix, StringComparison.Ordinal));

        Assert.Equal([TestConstants.AssemblyPrefix + "SharedKernel"], references);
    }

    [Fact]
    public void Abstractions_DoNotDependOnWinUi()
    {
        var references = ReferencedAssemblyNames(_abstractions);

        Assert.DoesNotContain(
            references, name => name.StartsWith("Microsoft.WinUI", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references, name => name.StartsWith("Microsoft.Windows.SDK", StringComparison.Ordinal));
    }

    [Fact]
    public void Abstractions_DoNotDependOnTheGeneratedContract()
    {
        var references = ReferencedAssemblyNames(_abstractions);

        Assert.DoesNotContain(TestConstants.ContractsAssembly, references);
    }
}
