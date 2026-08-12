using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kakehashi.Modules.Activity.Application;
using Xunit;

namespace Kakehashi.ArchitectureTests {
  // Per-module coverage lives with its module so adding or removing one never means editing the
  // shared LayeringTests.
  //
  // There is no Domain assembly here, and no test asserting one exists. The module is read-only —
  // the server is the only thing that appends to the feed — so there are no invariants to enforce
  // before a write, and an empty Domain project would be a layer added to satisfy a diagram.
  public sealed class ActivityLayeringTests {
    private static readonly Assembly _activityApplication =
        typeof(ActivityApplicationServiceCollectionExtensions).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly) {
      return assembly
          .GetReferencedAssemblies()
          .Select(reference => reference.Name ?? string.Empty)
          .ToList();
    }

    [Fact]
    public void Application_DoesNotDependOnTheUiOrHost() {
      var references = ReferencedAssemblyNames(_activityApplication);

      Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityLayer_DoesNotDependOnAnotherFeatureModule() {
      // The feed is assembled from other modules' facts, but that happens on the server, where the
      // activity module imports their api packages under archlint's eye. On the client the module
      // knows only its own contract, and must keep doing so as the feed covers more of the product.
      foreach (var name in ReferencedAssemblyNames(_activityApplication)) {
        if (name.StartsWith("Kakehashi.Modules.", StringComparison.Ordinal)) {
          Assert.StartsWith("Kakehashi.Modules.Activity.", name);
        }
      }
    }

    [Fact]
    public void Application_DoesNotDependOnTheGeneratedContract() {
      // Generated protobuf types are the wire's shape, not the module's: letting them past the UI
      // layer makes a schema change a use-case change, the coupling the gateway port prevents.
      var references = ReferencedAssemblyNames(_activityApplication);

      Assert.DoesNotContain(
          references, name => name.Equals("Kakehashi.Contracts", StringComparison.Ordinal));
      Assert.DoesNotContain(
          references, name => name.StartsWith("Grpc.", StringComparison.Ordinal));
      Assert.DoesNotContain(
          references, name => name.Equals("Google.Protobuf", StringComparison.Ordinal));
    }
  }
}
