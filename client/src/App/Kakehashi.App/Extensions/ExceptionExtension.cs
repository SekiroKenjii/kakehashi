using System;
using System.Collections.Generic;
using Kakehashi.App.Core;

namespace Kakehashi.App.Extensions {
  /// <summary>
  /// Extension methods for <see cref="System.Exception"/>.
  /// </summary>
  public static class ExceptionExtension {
    /// <summary>
    /// Flattens an exception and its inner exceptions into <see cref="CallStack"/> records suitable
    /// for display in the exception window.
    /// </summary>
    internal static IReadOnlyList<CallStack> ToCallStacks(this Exception exception) {
      ArgumentNullException.ThrowIfNull(exception);

      var stacks = new List<CallStack>();
      for (Exception? current = exception; current is not null; current = current.InnerException) {
        stacks.Add(new CallStack {
          ExceptionType = current.GetType().FullName ?? current.GetType().Name,
          Message = current.Message,
          Detail = new CallStackDetail {
            Module = current.Source ?? string.Empty,
            Method = current.TargetSite?.Name ?? string.Empty,
          },
        });
      }

      return stacks;
    }
  }
}
