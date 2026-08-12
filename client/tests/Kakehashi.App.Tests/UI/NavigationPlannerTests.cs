using System;
using System.Collections.Generic;
using System.Linq;
using Kakehashi.App.Services;
using Kakehashi.App.UI;
using Kakehashi.UI.Contracts;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.UI {
  // The planner joins two halves: what this build has, and where the deployment puts it. The
  // PlanWithoutALayout tests cover the fallback the pane uses before the deployment has answered,
  // which is the path an unreachable server takes and therefore the one nobody would notice was
  // broken.
  public sealed class NavigationPlannerTests {
    private readonly IModuleRegistry _registry = Substitute.For<IModuleRegistry>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    private static IModule Module(
        string name, string? assignmentId, params NavigationItem[] items) {
      return Module(name, assignmentId, isRequired: false, items);
    }

    private static IModule Module(
        string name, string? assignmentId, bool isRequired, params NavigationItem[] items) {
      var module = Substitute.For<IModule>();
      module.Name.Returns(name);
      module.Descriptor.Returns(new ModuleDescriptor(name, "", isRequired, assignmentId));
      module.GetNavigationItems().Returns(items);
      return module;
    }

    private static NavigationLayout Layout(params NavigationGroup[] groups) {
      return new NavigationLayout([], groups);
    }

    private static NavigationGroup Group(string title, params NavigationPlacement[] items) {
      return new NavigationGroup(title, items);
    }

    private static NavigationPlacement Placed(
        string id, string title = "", string icon = "", bool isEnabled = true) {
      return new NavigationPlacement(id, title, icon, isEnabled);
    }

    private NavigationPlanner CreatePlanner(params NavigationItem[] hostItems) {
      return new NavigationPlanner(_registry, _permissions, hostItems);
    }

    // The headings, their order, and the order within them all come from the deployment, not from
    // the order the modules happen to be mounted in.
    [Fact]
    public void Plan_DrawsTheHeadingsAndOrderTheDeploymentChose() {
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
    public void Plan_DrawsTheLabelTheDeploymentChose() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached(Arg.Any<string>()).Returns(true);

      var plan = CreatePlanner().Plan(Layout(Group("Tools", Placed("notes", title: "Scratchpad"))));

      Assert.Equal("Scratchpad", Assert.Single(plan).Item.Title);
    }

    // A deployment sends an icon NAME; which glyph draws it is this build's business. An unknown
    // name leaves the page's own glyph alone rather than blanking the row.
    [Fact]
    public void Plan_KeepsThisBuildsGlyphWhenTheIconNameMeansNothingToIt() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "compiled-glyph", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached(Arg.Any<string>()).Returns(true);

      var plan = CreatePlanner().Plan(
          Layout(Group("Tools", Placed("notes", icon: "a-name-from-a-newer-server"))));

      Assert.Equal("compiled-glyph", Assert.Single(plan).Item.IconGlyph);
    }

    // There is no page to open for a placement this build does not contain, so a row for it would
    // navigate nowhere.
    [Fact]
    public void Plan_SkipsAPlacementForSomethingThisBuildDoesNotHave() {
      _registry.All.Returns([]);

      var plan = CreatePlanner().Plan(
          Layout(Group("Utilities", Placed("a-module-this-build-lost"))));

      Assert.Empty(plan);
    }

    [Fact]
    public void Plan_DisablesADestinationTheDeploymentSaysTheAccountMayNotUse() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached(Arg.Any<string>()).Returns(true);

      var plan = CreatePlanner().Plan(
          Layout(Group("Tools", Placed("notes", isEnabled: false))));

      Assert.False(Assert.Single(plan).IsEnabled);
    }

    // Detaching is the user's own preference about their own composition, and no deployment is
    // entitled to overrule it.
    [Fact]
    public void Plan_OmitsADetachedModuleTheDeploymentPlaced() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached("Notes").Returns(false);

      var plan = CreatePlanner().Plan(Layout(Group("Tools", Placed("notes"))));

      Assert.Empty(plan);
    }

    // Both answers have to agree before a row works: the server's says whether the account may use
    // the destination, this client's whether an administrator withheld the whole module.
    [Fact]
    public void Plan_DisablesAWithheldModuleEvenWhenTheDeploymentEnabledIt() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      // Attached AND withheld: the point of the test is the disabled state, which presumes the user
      // has the module in their composition. Withholding alone no longer implies attachment.
      _registry.IsAttached("Notes").Returns(true);
      _registry.IsWithheld("notes").Returns(true);

      var plan = CreatePlanner().Plan(
          Layout(Group("Tools", Placed("notes", isEnabled: true))));

      Assert.False(Assert.Single(plan).IsEnabled);
    }

    // The footer avatar is not in the menu, so nothing about it is the deployment's to arrange. An
    // item with no id was never offered for arrangement and keeps the placement the client gave it.
    [Fact]
    public void Plan_KeepsAFooterItemTheDeploymentNeverPlaced() {
      var auth = Module("Auth", "account", isRequired: true,
          new NavigationItem("Account", "A", typeof(object), NavigationItemPlacement.Footer));
      _registry.All.Returns([auth]);

      var plan = CreatePlanner().Plan(Layout(Group("Utilities")));

      var entry = Assert.Single(plan);
      Assert.Equal("Account", entry.Item.Title);
      Assert.Equal(NavigationItemPlacement.Footer, entry.Item.Placement);
      Assert.True(entry.IsEnabled);
    }

    // Once the deployment has answered it is the authority on which destinations are offered: it
    // applied the same permission check server-side, with the grants it resolved itself. A second,
    // client-side check could only disagree with it.
    [Fact]
    public void Plan_DoesNotSecondGuessTheDeploymentWithItsOwnPermissionCheck() {
      _permissions.Allows(Arg.Any<string>()).Returns(false);

      var plan = CreatePlanner(new NavigationItem("Users", "U", typeof(object)) {
        Id = "account.users",
        RequiredPermission = "users.manage",
      }).Plan(Layout(Group("Administration", Placed("account.users"))));

      Assert.Single(plan);
    }

    [Fact]
    public void PlanWithoutALayout_UsesTheHeadingsAndOrderThisBuildDeclares() {
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

    // Absent, not disabled: this is the path taken when nothing is known, and a locked
    // administrative row offered to everybody is worse than a missing one.
    [Fact]
    public void PlanWithoutALayout_OmitsADestinationWhosePermissionTheAccountLacks() {
      _registry.All.Returns([]);
      _permissions.Allows("users.manage").Returns(false);

      var plan = CreatePlanner(new NavigationItem("Users", "U", typeof(object)) {
        Id = "account.users",
        RequiredPermission = "users.manage",
      }).Plan(NavigationLayout.None);

      Assert.Empty(plan);
    }

    [Fact]
    public void PlanWithoutALayout_DrawsAWithheldModuleDisabled() {
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
    public void PlanWithoutALayout_OmitsADetachedModule() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached("Notes").Returns(false);

      Assert.Empty(CreatePlanner().Plan(NavigationLayout.None));
    }

    // The sign-in module's page is how somebody signs out and manages their own account. An account
    // that cannot reach it is stuck, so withholding does not apply to it.
    [Fact]
    public void PlanWithoutALayout_KeepsARequiredModuleReachableEvenWhenWithheld() {
      var auth = Module("Auth", "account", isRequired: true,
          new NavigationItem("Account", "A", typeof(object), NavigationItemPlacement.Footer));
      _registry.All.Returns([auth]);
      _registry.IsWithheld("account").Returns(true);
      _registry.IsAttached("Auth").Returns(false);

      var entry = Assert.Single(CreatePlanner().Plan(NavigationLayout.None));

      Assert.Equal("Account", entry.Item.Title);
      Assert.True(entry.IsEnabled);
    }
    // The planner keys on the id, so a collision means one of the two pages never appears. It
    // resolves the clash deterministically rather than crashing, which makes this test the thing
    // that actually catches it — at build time, where a composition mistake belongs.
    [Fact]
    public void TheShippedDestinationIdsAreUnique() {
      var seen = new HashSet<string>(StringComparer.Ordinal);

      foreach (var item in HostNavigation.Items) {
        Assert.True(
            seen.Add(item.Id),
            $"two destinations claim the id {item.Id}; one of them will never appear");
      }
    }

    // The whole ungrouped branch was dead to this suite: the Layout helper hard-codes an empty
    // list, so nothing else here exercises the path the server has its own test for producing.
    [Fact]
    public void Plan_DrawsUngroupedDestinationsBeforeEveryHeading() {
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

    [Fact]
    public void Plan_UsesTheGlyphThisBuildKnowsForAName() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "compiled-glyph", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached(Arg.Any<string>()).Returns(true);

      var plan = CreatePlanner().Plan(Layout(Group("Tools", Placed("notes", icon: "note"))));

      Assert.NotEqual("compiled-glyph", Assert.Single(plan).Item.IconGlyph);
    }

    // Two different questions, and the old order conflated them: attachment was consulted only for
    // a module that was NOT withheld, so withholding a detached module put it back in the pane.
    [Fact]
    public void PlanWithoutALayout_KeepsADetachedModuleGoneWhenItIsAlsoWithheld() {
      var notes = Module("Notes", "notes",
          new NavigationItem("Notes", "N", typeof(object)) { Id = "notes" });
      _registry.All.Returns([notes]);
      _registry.IsAttached("Notes").Returns(false);
      _registry.IsWithheld("notes").Returns(true);

      Assert.Empty(CreatePlanner().Plan(NavigationLayout.None));
    }
  }
}
