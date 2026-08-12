using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kakehashi.Modules.Notes.Application;
using Kakehashi.Modules.Notes.Domain.Notes;
using Xunit;

namespace Kakehashi.ArchitectureTests {
  // Per-module coverage lives with its module so adding or removing one never means editing the
  // shared LayeringTests.
  public sealed class NotesLayeringTests {
    private static readonly Assembly _notesDomain = typeof(NoteDraft).Assembly;
    private static readonly Assembly _notesApplication =
        typeof(NotesApplicationServiceCollectionExtensions).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly) {
      return assembly
          .GetReferencedAssemblies()
          .Select(reference => reference.Name ?? string.Empty)
          .ToList();
    }

    [Fact]
    public void Domain_DoesNotDependOnApplicationUiHostOrMediator() {
      var references = ReferencedAssemblyNames(_notesDomain);

      Assert.DoesNotContain(
          references, name => name.Contains(".Application", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
      Assert.DoesNotContain(
          references, name => name.Equals("Kakehashi.Mediator", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_DoesNotDependOnTheUiOrHost() {
      var references = ReferencedAssemblyNames(_notesApplication);

      Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
    }

    [Fact]
    public void NotesLayer_DoesNotDependOnAnotherFeatureModule() {
      foreach (var assembly in new[] { _notesDomain, _notesApplication }) {
        foreach (var name in ReferencedAssemblyNames(assembly)) {
          if (name.StartsWith("Kakehashi.Modules.", StringComparison.Ordinal)) {
            Assert.StartsWith("Kakehashi.Modules.Notes.", name);
          }
        }
      }
    }

    [Fact]
    public void ApplicationAndDomain_DoNotDependOnTheGeneratedContract() {
      // Generated protobuf types are the wire's shape, not the module's: letting them past the UI
      // layer makes a schema change a use-case change, the coupling the gateway port prevents.
      // This is the client-side statement of the server rule that only rpc/ may import
      // internal/gen — archlint enforces it there, this test here, because the generated code lives
      // in one shared project rather than one per module.
      foreach (var assembly in new[] { _notesDomain, _notesApplication }) {
        var references = ReferencedAssemblyNames(assembly);

        Assert.DoesNotContain(
            references, name => name.Equals("Kakehashi.Contracts", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references, name => name.StartsWith("Grpc.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references, name => name.Equals("Google.Protobuf", StringComparison.Ordinal));
      }
    }
  }
}
