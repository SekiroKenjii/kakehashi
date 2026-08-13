using System.Collections.Generic;
using Kakehashi.SharedKernel;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Tracks which composed modules are part of the user's runtime composition. Every module stays
  /// compiled in with its services registered; attaching and detaching only changes what the shell
  /// and pages present. Each change is broadcast as a <see cref="ModuleSetChangedMessage"/> via the
  /// CommunityToolkit <c>WeakReferenceMessenger</c>.
  /// </summary>
  /// <remarks>
  /// Detaching is the user's reversible preference; an assignment is an administrator's permission.
  /// The two are separate states with their own keys, and the preference question is asked first:
  /// docs/adr/0015-module-attachment-is-not-a-security-boundary.md
  /// <para>
  /// <b>None of this is a security boundary.</b> The server refuses an unassigned module's
  /// requests.
  /// </para>
  /// </remarks>
  public interface IModuleRegistry {
    /// <summary>Every module composed into the application.</summary>
    IReadOnlyList<IModule> All { get; }

    /// <summary>The modules currently part of the user's composition.</summary>
    IReadOnlyList<IModule> Attached { get; }

    /// <summary>Whether the named module is attached. Unknown names are not attached.</summary>
    bool IsAttached(string moduleName);

    /// <summary>
    /// Whether an administrator has withheld this module from the signed-in account. Such a module
    /// is never attached, and attaching it fails.
    /// </summary>
    bool IsWithheld(string moduleName);

    /// <summary>
    /// Whether an administrator granted this module, rather than it simply being ungoverned. A
    /// granted module is attached and cannot be detached: the grant is not the user's to give back.
    /// </summary>
    bool IsGranted(string moduleName);

    /// <summary>Attaches a detached module. Fails for unknown names and for withheld ones.</summary>
    Result Attach(string moduleName);

    /// <summary>
    /// Detaches a module. Fails for unknown names, for required modules, and for granted ones.
    /// </summary>
    Result Detach(string moduleName);

    /// <summary>
    /// Replaces what the server says this account may use, and broadcasts the change.
    /// </summary>
    /// <param name="withheld">Server module ids the account is not assigned.</param>
    /// <param name="granted">Server module ids an administrator granted.</param>
    /// <remarks>
    /// Called once after sign-in. Before it is called both sets are empty, which reproduces exactly
    /// the behaviour of a build without assignments — so a fetch that never returns leaves the app
    /// as it was rather than empty, and the server is still the thing that refuses.
    /// </remarks>
    void SetAssignments(IReadOnlyCollection<string> withheld, IReadOnlyCollection<string> granted);
  }
}
