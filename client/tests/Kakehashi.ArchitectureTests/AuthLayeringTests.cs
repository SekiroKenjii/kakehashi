using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kakehashi.Modules.Auth.Application;
using Kakehashi.Modules.Auth.Domain;
using Xunit;

namespace Kakehashi.ArchitectureTests {
  /// <summary>
  /// Auth-module counterparts to <see cref="LayeringTests"/>. Kept in a separate file (with its own
  /// referenced project assemblies) so the Auth module and its architecture coverage can be removed
  /// as a unit by the setup script, without editing the shared <see cref="LayeringTests"/>.
  /// </summary>
  public sealed class AuthLayeringTests {
    private static readonly Assembly _authDomain = typeof(AuthSession).Assembly;
    private static readonly Assembly _authApplication =
        typeof(AuthApplicationServiceCollectionExtensions).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly) {
      return assembly
          .GetReferencedAssemblies()
          .Select(reference => reference.Name ?? string.Empty)
          .ToList();
    }

    [Fact]
    public void Domain_DoesNotDependOnApplicationUiHostOrMediator() {
      var references = ReferencedAssemblyNames(_authDomain);

      Assert.DoesNotContain(
          references, name => name.Contains(".Application", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
      Assert.DoesNotContain(
          references, name => name.Equals("Kakehashi.Mediator", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_DoesNotDependOnTheUiOrHost() {
      var references = ReferencedAssemblyNames(_authApplication);

      Assert.DoesNotContain(references, name => name.Contains(".UI", StringComparison.Ordinal));
      Assert.DoesNotContain(references, name => name.EndsWith(".App", StringComparison.Ordinal));
    }

    [Fact]
    public void AuthLayer_DoesNotDependOnAnotherFeatureModule() {
      foreach (var assembly in new[] { _authDomain, _authApplication }) {
        foreach (var name in ReferencedAssemblyNames(assembly)) {
          if (name.StartsWith("Kakehashi.Modules.", StringComparison.Ordinal)) {
            Assert.StartsWith("Kakehashi.Modules.Auth.", name);
          }
        }
      }
    }
  }
}
