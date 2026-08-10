namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Presentation metadata a module contributes about itself. The module's identity stays
  /// <see cref="IModule.Name"/>; this record only describes it to the user.
  /// </summary>
  /// <param name="DisplayName">The user-facing module name (home-page tiles, attach dialog).</param>
  /// <param name="Description">A one-sentence description shown on tiles and in dialogs.</param>
  /// <param name="IsRequired">
  /// Required modules are always attached and can never be detached (e.g. the Auth module, which
  /// the startup sign-in gate depends on).
  /// </param>
  /// <param name="AssignmentId">
  /// The server's module id, when this module has a server half whose access an administrator
  /// governs. Null means the module is local-only and nobody can lock it.
  /// <para>
  /// It is separate from <see cref="IModule.Name"/> because the two genuinely differ: the client
  /// calls its sign-in module "Auth" while the server calls the module behind it "account". A
  /// permission key that quietly drifts between the halves is a permission that quietly stops
  /// applying, so the mapping is written down here rather than guessed from the display name.
  /// </para>
  /// </param>
  public sealed record ModuleDescriptor(
      string DisplayName, string Description, bool IsRequired, string? AssignmentId = null);
}
