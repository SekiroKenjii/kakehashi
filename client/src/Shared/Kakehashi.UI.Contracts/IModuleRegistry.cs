using System.Collections.Generic;
using Kakehashi.SharedKernel;

namespace Kakehashi.UI.Contracts {
  // Every module stays compiled in with its services registered; attaching and detaching only
  // changes what the shell and pages present. Each change is broadcast as a
  // ModuleSetChangedMessage via the CommunityToolkit WeakReferenceMessenger.
  //
  // Two different reasons a module may be unavailable, and they must not be confused. Detaching is
  // a preference: the user's own, reversible in a click. An assignment is a permission: decided by
  // an administrator, and the only thing here the user cannot overrule.
  //
  // None of this is a security boundary. The server refuses an unassigned module's requests on its
  // own, at one place that sees every request, whatever this client believes. What lives here is
  // the courtesy of drawing a lock instead of a button that is going to fail.
  public interface IModuleRegistry {
    IReadOnlyList<IModule> All { get; }

    IReadOnlyList<IModule> Attached { get; }

    // An unknown name is not attached rather than an error.
    bool IsAttached(string moduleName);

    // Withheld by an administrator: never attached, and attaching fails.
    bool IsWithheld(string moduleName);

    // Granted by an administrator, as opposed to simply ungoverned: attached and undetachable,
    // because the grant is not the user's to give back.
    bool IsGranted(string moduleName);

    // Fails for unknown names and for withheld modules.
    Result Attach(string moduleName);

    // Fails for unknown names, for required modules, and for granted ones.
    Result Detach(string moduleName);

    // Called once after sign-in, with the server's module ids. Before that both sets are empty,
    // which reproduces exactly a build without assignments: a fetch that never returns leaves the
    // app as it was rather than empty, and the server is still the thing that refuses.
    void SetAssignments(IReadOnlyCollection<string> withheld, IReadOnlyCollection<string> granted);
  }
}
