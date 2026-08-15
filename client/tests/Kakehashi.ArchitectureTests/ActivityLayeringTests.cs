using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kakehashi.Modules.Activity.Application;
using Xunit;

namespace Kakehashi.ArchitectureTests;

/// <summary>
/// Activity-module counterparts to <see cref="LayeringTests"/>. Per-module coverage lives with
/// its module so that adding or removing one never means editing the shared file.
/// </summary>
/// <remarks>
/// There is no Domain assembly here, and no test asserting one exists. The module is read-only —
/// the server is the only thing that appends to the feed — so there are no invariants to enforce
/// before a write, and an empty Domain project would be a layer added to satisfy a diagram.
/// </remarks>
public sealed class ActivityLayeringTests
{
    private static readonly Assembly _activityApplication =
        typeof(ActivityApplicationServiceCollectionExtensions).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void Application_DoesNotDependOnTheUiOrHost()
    {
        var references = ReferencedAssemblyNames(_activityApplication);

        Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityLayer_DoesNotDependOnAnotherFeatureModule()
    {
        // The feed is assembled from other modules' facts, but on the server, under archlint's eye.
        // Here the module knows only its own contract, however far the feed grows.
        foreach (var name in ReferencedAssemblyNames(_activityApplication))
        {
            if (name.StartsWith(TestConstants.ModulesPrefix, StringComparison.Ordinal))
            {
                Assert.StartsWith(TestConstants.ModulesPrefix + "Activity.", name);
            }
        }
    }

    [Fact]
    public void Application_DoesNotDependOnTheGeneratedContract()
    {
        // Generated protobuf types are the wire's shape, not the module's: past the UI layer a
        // schema change becomes a use-case change, the coupling the gateway port exists to prevent.
        var references = ReferencedAssemblyNames(_activityApplication);

        Assert.DoesNotContain(
            references, name => name.Equals(TestConstants.ContractsAssembly, StringComparison.Ordinal));
        Assert.DoesNotContain(
            references, name => name.StartsWith("Grpc.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references, name => name.Equals("Google.Protobuf", StringComparison.Ordinal));
    }
}
