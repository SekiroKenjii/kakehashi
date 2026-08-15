using System;
using System.Collections.Generic;
using System.Linq;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.App.UI;
using __ROOT_NAMESPACE__.UI.Contracts;
using NSubstitute;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.UI;

/// <summary>
/// Unit tests for <see cref="NavigationPlanner"/>: which destinations reach the pane, in what order,
/// under which heading, and which of them are reachable.
/// </summary>
/// <remarks>
/// The class joins two halves — what this build has, and where the deployment puts it — so the tests
/// come in two sets. One set covers the join. The other covers the fallback the pane uses before the
/// deployment has answered, which is the path an unreachable server takes and therefore the one
/// nobody would notice was broken.
/// </remarks>
public sealed class NavigationPlannerTests
{
    private readonly IModuleRegistry _registry = Substitute.For<IModuleRegistry>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    private static IModule Module(
        string name, string? assignmentId, params NavigationItem[] items)
    {
        return Module(name, assignmentId, isRequired: false, items);
    }

    private static IModule Module(
        string name, string? assignmentId, bool isRequired, params NavigationItem[] items)
    {
        var module = Substitute.For<IModule>();
        module.Name.Returns(name);
        module.Descriptor.Returns(new ModuleDescriptor(name, "", isRequired, assignmentId));
        module.GetNavigationItems().Returns(items);

        return module;
    }

    private static NavigationLayout Layout(params NavigationGroup[] groups)
    {
        return new NavigationLayout([], groups);
    }

    private static NavigationGroup Group(string title, params NavigationPlacement[] items)
    {
        return new NavigationGroup(title, items);
    }

    private static NavigationPlacement Placed(
        string id, string title = "", string icon = "", bool isEnabled = true)
    {
        return new NavigationPlacement(id, title, icon, isEnabled);
    }

    private NavigationPlanner CreatePlanner(params NavigationItem[] hostItems)
    {
        return new NavigationPlanner(_registry, _permissions, hostItems);
    }

    // --- The join: the deployment decides the menu, this build decides what it has. ---

    /// <summary>
    /// The headings, their order, and the order within them all come from the deployment — not from
    /// the order the modules happen to be mounted in.
    /// </summary>
    [Fact]
    public void Plan_DrawsTheHeadingsAndOrderTheDeploymentChose()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes", Group = "Utilities" });
        var activity = Module("Activity", "activity",
            new NavigationItem("Activity", "A", typeof(object)) {
                Id = "activity",
                Group = "Utilities",
            });
        _registry.All.Returns([notes, activity]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);

        // Mounted notes-then-activity; arranged activity-then-notes, under a heading of the
        // deployment's own naming.
        var plan = CreatePlanner().Plan(
            Layout(Group("Tools", Placed("activity"), Placed("notes"))));

        Assert.Equal(["Activity", "Notes"], plan.Select(entry => entry.Item.Title));
        Assert.All(plan, entry => Assert.Equal("Tools", entry.Item.Group));
    }

    [Fact]
    public void Plan_DrawsTheLabelTheDeploymentChose()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);

        var plan = CreatePlanner().Plan(Layout(Group("Tools", Placed("notes", title: "Scratchpad"))));

        Assert.Equal("Scratchpad", Assert.Single(plan).Item.Title);
    }

    /// <summary>
    /// A deployment sends an icon NAME; which glyph draws it is this build's business. An unknown name
    /// leaves the page's own glyph alone rather than blanking the row.
    /// </summary>
    [Fact]
    public void Plan_KeepsThisBuildsGlyphWhenTheIconNameMeansNothingToIt()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "compiled-glyph", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);

        var plan = CreatePlanner().Plan(
            Layout(Group("Tools", Placed("notes", icon: "a-name-from-a-newer-server"))));

        Assert.Equal("compiled-glyph", Assert.Single(plan).Item.IconGlyph);
    }

    /// <summary>
    /// A layout naming a page this build does not contain is skipped. There is no page to open, so a
    /// row for it would navigate nowhere.
    /// </summary>
    [Fact]
    public void Plan_SkipsAPlacementForSomethingThisBuildDoesNotHave()
    {
        _registry.All.Returns([]);

        var plan = CreatePlanner().Plan(
            Layout(Group("Utilities", Placed("a-module-this-build-lost"))));

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_DisablesADestinationTheDeploymentSaysTheAccountMayNotUse()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);

        var plan = CreatePlanner().Plan(
            Layout(Group("Tools", Placed("notes", isEnabled: false))));

        Assert.False(Assert.Single(plan).IsEnabled);
    }

    /// <summary>
    /// Detaching is the user's own preference about their own composition, and no deployment is
    /// entitled to overrule it.
    /// </summary>
    [Fact]
    public void Plan_OmitsADetachedModuleTheDeploymentPlaced()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached("Notes").Returns(false);

        var plan = CreatePlanner().Plan(Layout(Group("Tools", Placed("notes"))));

        Assert.Empty(plan);
    }

    /// <summary>
    /// Both answers have to agree before a row works. The server's says whether the account may use
    /// the destination; this client's says whether an administrator withheld the whole module.
    /// </summary>
    [Fact]
    public void Plan_DisablesAWithheldModuleEvenWhenTheDeploymentEnabledIt()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        // Attached AND withheld: the point of the test is the disabled state, which presumes the user
        // has the module in their composition. Withholding alone does not imply attachment.
        _registry.IsAttached("Notes").Returns(true);
        _registry.IsWithheld("notes").Returns(true);

        var plan = CreatePlanner().Plan(
            Layout(Group("Tools", Placed("notes", isEnabled: true))));

        Assert.False(Assert.Single(plan).IsEnabled);
    }

    /// <summary>
    /// The footer avatar is not in the menu, so nothing about it is the deployment's to arrange. An
    /// item with no id was never offered for arrangement and keeps the placement the client gave it.
    /// </summary>
    [Fact]
    public void Plan_KeepsAFooterItemTheDeploymentNeverPlaced()
    {
        var auth = Module("Auth", "account", isRequired: true,
            new NavigationItem("Account", "A", typeof(object), NavigationItemPlacement.Footer));
        _registry.All.Returns([auth]);

        var plan = CreatePlanner().Plan(Layout(Group("Utilities")));

        var entry = Assert.Single(plan);
        Assert.Equal("Account", entry.Item.Title);
        Assert.Equal(NavigationItemPlacement.Footer, entry.Item.Placement);
        Assert.True(entry.IsEnabled);
    }

    /// <summary>
    /// Once the deployment has answered, it is the authority on which destinations are offered — it
    /// applied the same permission check server-side, with the grants it resolved itself. A second,
    /// client-side check could only disagree with it.
    /// </summary>
    [Fact]
    public void Plan_DoesNotSecondGuessTheDeploymentWithItsOwnPermissionCheck()
    {
        _permissions.Allows(Arg.Any<string>()).Returns(false);

        var plan = CreatePlanner(new NavigationItem("Users", "U", typeof(object)) {
            Id = "account.users",
            RequiredPermission = "users.manage",
        }).Plan(Layout(Group("Administration", Placed("account.users"))));

        Assert.Single(plan);
    }

    // --- The fallback: before the deployment has answered, or when it cannot be reached. ---

    [Fact]
    public void PlanWithoutALayout_UsesTheHeadingsAndOrderThisBuildDeclares()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes", Group = "Utilities" });
        var activity = Module("Activity", "activity",
            new NavigationItem("Activity", "A", typeof(object)) {
                Id = "activity",
                Group = "Utilities",
            });
        _registry.All.Returns([notes, activity]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);
        _permissions.Allows(Arg.Any<string>()).Returns(true);

        var plan = CreatePlanner(new NavigationItem("Users", "U", typeof(object)) {
            Id = "account.users",
            Group = "Administration",
        }).Plan(NavigationLayout.None);

        Assert.Equal(["Notes", "Activity", "Users"], plan.Select(entry => entry.Item.Title));
        Assert.Equal(
            ["Utilities", "Utilities", "Administration"], plan.Select(entry => entry.Item.Group));
    }

    /// <summary>
    /// Absent, not disabled. This is the path taken when nothing is known, and a locked
    /// administrative row offered to everybody is worse than a missing one.
    /// </summary>
    [Fact]
    public void PlanWithoutALayout_OmitsADestinationWhosePermissionTheAccountLacks()
    {
        _registry.All.Returns([]);
        _permissions.Allows("users.manage").Returns(false);

        var plan = CreatePlanner(new NavigationItem("Users", "U", typeof(object)) {
            Id = "account.users",
            RequiredPermission = "users.manage",
        }).Plan(NavigationLayout.None);

        Assert.Empty(plan);
    }

    [Fact]
    public void PlanWithoutALayout_DrawsAWithheldModuleDisabled()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached("Notes").Returns(true);
        _registry.IsWithheld("notes").Returns(true);

        var plan = CreatePlanner().Plan(NavigationLayout.None);

        var entry = Assert.Single(plan);
        Assert.Equal("Notes", entry.Item.Title);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void PlanWithoutALayout_OmitsADetachedModule()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached("Notes").Returns(false);

        Assert.Empty(CreatePlanner().Plan(NavigationLayout.None));
    }

    /// <summary>
    /// The sign-in module's page is how somebody signs out and manages their own account. An account
    /// that cannot reach it is stuck, so withholding does not apply to it.
    /// </summary>
    [Fact]
    public void PlanWithoutALayout_KeepsARequiredModuleReachableEvenWhenWithheld()
    {
        var auth = Module("Auth", "account", isRequired: true,
            new NavigationItem("Account", "A", typeof(object), NavigationItemPlacement.Footer));
        _registry.All.Returns([auth]);
        _registry.IsWithheld("account").Returns(true);
        _registry.IsAttached("Auth").Returns(false);

        var entry = Assert.Single(CreatePlanner().Plan(NavigationLayout.None));

        Assert.Equal("Account", entry.Item.Title);
        Assert.True(entry.IsEnabled);
    }
    /// <summary>
    /// No two destinations this build ships may claim the same id.
    /// </summary>
    /// <remarks>
    /// The planner keys on the id, so a collision means one of the two pages never appears. It
    /// resolves the clash deterministically rather than crashing, which makes this test the thing
    /// that actually catches it — at build time, where a composition mistake belongs.
    /// </remarks>
    [Fact]
    public void TheShippedDestinationIdsAreUnique()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in HostNavigation.Items)
        {
            Assert.True(
                seen.Add(item.Id),
                $"two destinations claim the id {item.Id}; one of them will never appear");
        }
    }

    /// <summary>
    /// The deployment may place a destination with no heading, and those are drawn above every group.
    /// </summary>
    /// <remarks>
    /// The whole ungrouped branch was dead to this suite: the Layout helper hard-coded an empty
    /// list, so nothing here ever exercised the path the server has its own test for producing.
    /// </remarks>
    [Fact]
    public void Plan_DrawsUngroupedDestinationsBeforeEveryHeading()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        var activity = Module("Activity", "activity",
            new NavigationItem("Activity", "A", typeof(object)) { Id = "activity" });
        _registry.All.Returns([notes, activity]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);

        var plan = CreatePlanner().Plan(new NavigationLayout(
            [Placed("activity")],
            [Group("Utilities", Placed("notes"))]));

        Assert.Equal(["Activity", "Notes"], plan.Select(entry => entry.Item.Title));
        Assert.Equal(string.Empty, plan[0].Item.Group);
        Assert.Equal("Utilities", plan[1].Item.Group);
    }

    /// <summary>A known icon name resolves to the glyph this build draws it with.</summary>
    [Fact]
    public void Plan_UsesTheGlyphThisBuildKnowsForAName()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "compiled-glyph", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached(Arg.Any<string>()).Returns(true);

        var plan = CreatePlanner().Plan(Layout(Group("Tools", Placed("notes", icon: "note"))));

        Assert.NotEqual("compiled-glyph", Assert.Single(plan).Item.IconGlyph);
    }

    /// <summary>
    /// A module the user detached stays gone even once an administrator withholds it.
    /// </summary>
    /// <remarks>
    /// Detachment and withholding are separate questions, asked in that order; consulting
    /// attachment only for a module that is not withheld would put a detached module back in the
    /// pane: docs/adr/0015-module-attachment-is-not-a-security-boundary.md
    /// </remarks>
    [Fact]
    public void PlanWithoutALayout_KeepsADetachedModuleGoneWhenItIsAlsoWithheld()
    {
        var notes = Module("Notes", "notes",
            new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
        _registry.All.Returns([notes]);
        _registry.IsAttached("Notes").Returns(false);
        _registry.IsWithheld("notes").Returns(true);

        Assert.Empty(CreatePlanner().Plan(NavigationLayout.None));
    }
}
