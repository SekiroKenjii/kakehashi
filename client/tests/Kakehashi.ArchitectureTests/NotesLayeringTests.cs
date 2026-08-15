using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kakehashi.Modules.Notes.Application;
using Kakehashi.Modules.Notes.Domain.Notes;
using Xunit;

namespace Kakehashi.ArchitectureTests;

/// <summary>
/// Notes-module counterparts to <see cref="LayeringTests"/>. Per-module coverage lives with its
/// module so that adding or removing one never means editing the shared file.
/// </summary>
public sealed class NotesLayeringTests
{
    private static readonly Assembly _notesDomain = typeof(NoteDraft).Assembly;
    private static readonly Assembly _notesApplication =
        typeof(NotesApplicationServiceCollectionExtensions).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void Domain_DoesNotDependOnApplicationUiHostOrMediator()
    {
        var references = ReferencedAssemblyNames(_notesDomain);

        Assert.DoesNotContain(
            references, name => name.Contains(".Application", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references, name => name.Equals(TestConstants.MediatorAssembly, StringComparison.Ordinal));
    }

    [Fact]
    public void Application_DoesNotDependOnTheUiOrHost()
    {
        var references = ReferencedAssemblyNames(_notesApplication);

        Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
    }

    [Fact]
    public void NotesLayer_DoesNotDependOnAnotherFeatureModule()
    {
        foreach (var assembly in new[] { _notesDomain, _notesApplication })
        {
            foreach (var name in ReferencedAssemblyNames(assembly))
            {
                if (name.StartsWith(TestConstants.ModulesPrefix, StringComparison.Ordinal))
                {
                    Assert.StartsWith(TestConstants.ModulesPrefix + "Notes.", name);
                }
            }
        }
    }

    [Fact]
    public void ApplicationAndDomain_DoNotDependOnTheGeneratedContract()
    {
        // The client-side statement of the server's rule that only rpc/ may import internal/gen.
        // A test rather than archlint, because the generated code is one shared project here.
        foreach (var assembly in new[] { _notesDomain, _notesApplication })
        {
            var references = ReferencedAssemblyNames(assembly);

            Assert.DoesNotContain(
                references, name => name.Equals(TestConstants.ContractsAssembly, StringComparison.Ordinal));
            Assert.DoesNotContain(
                references, name => name.StartsWith("Grpc.", StringComparison.Ordinal));
            Assert.DoesNotContain(
                references, name => name.Equals("Google.Protobuf", StringComparison.Ordinal));
        }
    }
}
