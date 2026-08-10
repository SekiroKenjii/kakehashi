using System.Collections.Generic;
using Kakehashi.Modules.Activity.UI;
using Kakehashi.Modules.Auth.UI;
using Kakehashi.Modules.Notes.UI;
using Kakehashi.UI.Contracts;

namespace Kakehashi.App.Composition {
  /// <summary>
  /// The single place that lists the feature modules composed into the application. Add a module by
  /// referencing its UI project and adding its <see cref="IModule"/> here.
  /// </summary>
  internal static class ModuleCatalog {
    public static IReadOnlyList<IModule> Modules { get; } = [
      new NotesModule(),
      new ActivityModule(),
      new AuthModule(),
    ];
  }
}
