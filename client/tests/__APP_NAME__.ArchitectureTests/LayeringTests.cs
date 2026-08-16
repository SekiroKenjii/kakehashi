using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using __ROOT_NAMESPACE__.SharedKernel;
using Xunit;

namespace __ROOT_NAMESPACE__.ArchitectureTests;

/// <summary>
/// Enforces the modular-monolith dependency rules that hold no matter which feature modules are
/// composed in. Per-module layering lives alongside its module (see
/// <see cref="AuthLayeringTests"/>), so adding or removing a module never means editing this
/// file. Checks inspect referenced assembly names (no assemblies are loaded), which keeps these
/// tests fast and free of the WinUI layers.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly _sharedKernel = typeof(Result).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void SharedKernel_DependsOnlyOnTheBaseClassLibrary()
    {
        var references = ReferencedAssemblyNames(_sharedKernel);

        Assert.DoesNotContain(
            references, name => name.StartsWith(TestConstants.AssemblyPrefix, StringComparison.Ordinal));
    }
}
