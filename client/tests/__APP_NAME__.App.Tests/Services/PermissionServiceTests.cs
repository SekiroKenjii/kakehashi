using System.Collections.Generic;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.UI.Contracts;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Services;

/// <summary>
/// Unit tests for the one decision <see cref="PermissionService"/> makes about module access:
/// which modules a grant set withholds.
/// </summary>
/// <remarks>
/// The fetch around it cannot be substituted — the generated gRPC client returns
/// <c>AsyncUnaryCall</c> — so the rule is pinned here and the transport stays covered by the live
/// verification, the same split <c>NavigationLayoutServiceTests</c> documents.
/// </remarks>
public sealed class PermissionServiceTests
{
    private static readonly Dictionary<string, string> _noGrants = [];

    [Fact]
    public void ARequiredModuleIsNeverWithheld()
    {
        // The regression: the account module is required and carries an assignment id, but nothing
        // server-side mints account.access — no route checks it, so it is not in the catalogue and
        // no administrator can assign it. Waiting for it locked the account page for everybody.
        var account = new ModuleDescriptor(
            "Account", "Identity and sessions.", IsRequired: true, AssignmentId: "account");

        Assert.False(PermissionService.Withholds(account, _noGrants));
    }

    [Fact]
    public void AnOptionalModuleIsWithheldWithoutItsAccessGrant()
    {
        var notes = new ModuleDescriptor(
            "Notes", "The example module.", IsRequired: false, AssignmentId: "notes");

        Assert.True(PermissionService.Withholds(notes, _noGrants));
    }

    [Fact]
    public void AnOptionalModuleIsNotWithheldOnceItsAccessGrantArrives()
    {
        var notes = new ModuleDescriptor(
            "Notes", "The example module.", IsRequired: false, AssignmentId: "notes");
        var grants = new Dictionary<string, string> { ["notes.access"] = "all" };

        Assert.False(PermissionService.Withholds(notes, grants));
    }

    [Fact]
    public void AModuleWithNoAssignmentIdIsNeverWithheld()
    {
        // Nothing on the server names it, so there is no grant that could arrive for it.
        var local = new ModuleDescriptor("Local", "Client only.", IsRequired: false);

        Assert.False(PermissionService.Withholds(local, _noGrants));
    }

    [Fact]
    public void AnotherModulesGrantDoesNotUnlockThisOne()
    {
        var notes = new ModuleDescriptor(
            "Notes", "The example module.", IsRequired: false, AssignmentId: "notes");
        var grants = new Dictionary<string, string> { ["activity.access"] = "all" };

        Assert.True(PermissionService.Withholds(notes, grants));
    }
}
