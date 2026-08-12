using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kakehashi.SharedKernel;
using Xunit;

namespace Kakehashi.ArchitectureTests {
  // Only the rules that hold no matter which feature modules are composed in; per-module layering
  // lives alongside its module (see AuthLayeringTests), so adding or removing one never means
  // editing this file. Checks read referenced assembly names rather than loading assemblies, which
  // keeps them off the WinUI layers.
  public sealed class LayeringTests {
    private static readonly Assembly _sharedKernel = typeof(Result).Assembly;

    private static IReadOnlyList<string> ReferencedAssemblyNames(Assembly assembly) {
      return assembly
          .GetReferencedAssemblies()
          .Select(reference => reference.Name ?? string.Empty)
          .ToList();
    }

    [Fact]
    public void SharedKernel_DependsOnlyOnTheBaseClassLibrary() {
      var references = ReferencedAssemblyNames(_sharedKernel);

      Assert.DoesNotContain(
          references, name => name.StartsWith("Kakehashi.", StringComparison.Ordinal));
    }
  }
}
