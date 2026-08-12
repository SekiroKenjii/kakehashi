namespace Kakehashi.UI.Contracts {
  // Description only: the module's identity stays IModule.Name.
  //
  // IsRequired modules are always attached and can never be detached, like the Auth module the
  // startup sign-in gate depends on.
  //
  // AssignmentId is the server's module id, for a module with a server half whose access an
  // administrator governs; null means local-only and nobody can lock it. It is separate from
  // IModule.Name because the two genuinely differ - the client calls its sign-in module "Auth"
  // while the server calls the module behind it "account" - and a permission key that quietly
  // drifts between the halves is a permission that quietly stops applying.
  public sealed record ModuleDescriptor(
      string DisplayName, string Description, bool IsRequired, string? AssignmentId = null);
}
