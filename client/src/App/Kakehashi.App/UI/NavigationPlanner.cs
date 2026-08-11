using System;
using System.Collections.Generic;
using System.Linq;
using Kakehashi.App.Services;
using Kakehashi.UI.Common.Controls;
using Kakehashi.UI.Contracts;

namespace Kakehashi.App.UI {
  /// <summary>One pane entry, and whether the account may use it.</summary>
  public sealed record NavigationEntry(NavigationItem Item, bool IsEnabled);

  /// <summary>
  /// Decides what goes in the navigation pane, in what order, and what is reachable.
  /// </summary>
  /// <remarks>
  /// Its own class rather than a method on the shell, because these are rules worth being able to
  /// test — and a <c>Page</c> cannot be constructed off the UI thread, so a rule that lives on one is
  /// a rule nothing checks. The shell keeps what is genuinely its own: turning entries into controls.
  /// <para>
  /// Two halves meet here. The client knows which destinations it <em>has</em> — a destination is a
  /// compiled page, and no server can conjure one. The deployment knows where they <em>go</em> —
  /// which heading, in what order, under what label, and whether an account may use them. This class
  /// is the join, and it trusts each side for exactly its half.
  /// </para>
  /// </remarks>
  public sealed class NavigationPlanner {
    private readonly IModuleRegistry _registry;
    private readonly IPermissionService _permissions;
    private readonly IReadOnlyList<NavigationItem> _hostItems;

    public NavigationPlanner(IModuleRegistry registry, IPermissionService permissions)
        : this(registry, permissions, HostNavigation.Items) {
    }

    /// <summary>Overload taking the host's items, so a test can supply its own.</summary>
    public NavigationPlanner(
        IModuleRegistry registry,
        IPermissionService permissions,
        IReadOnlyList<NavigationItem> hostItems) {
      ArgumentNullException.ThrowIfNull(registry);
      ArgumentNullException.ThrowIfNull(permissions);
      ArgumentNullException.ThrowIfNull(hostItems);
      _registry = registry;
      _permissions = permissions;
      _hostItems = hostItems;
    }

    /// <summary>
    /// The pane's entries: the deployment's menu first, then the destinations the client places
    /// itself.
    /// </summary>
    /// <remarks>
    /// The deployment decides the menu — the order of the headings, the order within them, the labels,
    /// and which destinations an account is offered at all. This client contributes three things it is
    /// the only one able to know:
    /// <list type="bullet">
    /// <item>whether it <em>has</em> the destination. A layout naming a page this build does not
    /// contain is skipped rather than drawn as a row that navigates nowhere;</item>
    /// <item>whether the user <em>detached</em> its module. That is their own preference about their
    /// own composition, and no server is entitled to overrule it;</item>
    /// <item>where the footer items go. The account avatar is not in the menu, so nothing about it is
    /// the deployment's to arrange.</item>
    /// </list>
    /// <para>
    /// With no layout — the first run, or a server that could not be reached — it falls back to
    /// <see cref="PlanLocally"/>. An app that will not draw a menu because a call timed out is worse
    /// than one drawing the menu it was built with.
    /// </para>
    /// </remarks>
    public IReadOnlyList<NavigationEntry> Plan(NavigationLayout layout) {
      ArgumentNullException.ThrowIfNull(layout);

      var available = Available();
      if (layout.IsEmpty) {
        return PlanLocally(available);
      }

      var entries = new List<NavigationEntry>();
      foreach (var placement in layout.Ungrouped) {
        Place(entries, available, placement, group: string.Empty);
      }
      foreach (var group in layout.Groups) {
        foreach (var placement in group.Items) {
          Place(entries, available, placement, group.Title);
        }
      }

      // The footer, which the deployment has no say over: an item with no id was never offered for
      // arrangement, so it keeps the placement the client gave it.
      foreach (var (item, isEnabled) in available.Values.Where(entry => entry.Item.Id.Length == 0)) {
        entries.Add(new NavigationEntry(item, isEnabled));
      }
      return entries;
    }

    /// <summary>
    /// The pane as this build alone would draw it, used until the deployment's answer arrives.
    /// </summary>
    /// <remarks>
    /// Three rules, and they are the client-side approximation of what the server does properly:
    /// <list type="bullet">
    /// <item>a destination whose permission the account lacks is <em>absent</em>. It is the cautious
    /// direction — this is the path taken when nothing is known, and a locked administrative row
    /// offered to everybody is worse than a missing one;</item>
    /// <item>a module an administrator withheld is present and <em>disabled</em>, because a module the
    /// product has and this account has not been given is worth being able to see and ask for;</item>
    /// <item>a module the user detached is absent, because that is their own preference.</item>
    /// </list>
    /// A required module — the one the sign-in gate depends on — is exempt from withholding. Its page
    /// is how somebody signs out and manages their own account, and an account that cannot reach it is
    /// stuck.
    /// </remarks>
    private IReadOnlyList<NavigationEntry> PlanLocally(
        IReadOnlyDictionary<string, NavigationEntry> available) {
      var entries = new List<NavigationEntry>();
      foreach (var (item, isEnabled) in available.Values) {
        if (!Permits(item)) {
          continue;
        }
        entries.Add(new NavigationEntry(item, isEnabled));
      }
      return entries;
    }

    /// <summary>
    /// The compiled navigation item a stored row refers to, or null when this build has no such page.
    /// </summary>
    /// <remarks>
    /// Exposed for the layout screen, which reports a screen's route and where it is declared. Those
    /// come from the page type this build compiled, not from the server — the server has no notion of a
    /// route, and no way to know which file declares a page. Reusing this join is what keeps the screen
    /// from re-deriving a mapping the planner already owns.
    /// </remarks>
    public NavigationItem? Find(string id) {
      if (id.Length == 0) {
        return null;
      }
      return Available().TryGetValue(id, out var entry) ? entry.Item : null;
    }

    /// <summary>
    /// Every destination this client has and the user has not detached, keyed by its id.
    /// </summary>
    /// <remarks>
    /// Insertion-ordered, because <see cref="PlanLocally"/> draws them in this order and the
    /// composition root's module order is the sensible fallback. The footer items have no id, so they
    /// are keyed by their page type to keep them from colliding.
    /// </remarks>
    private Dictionary<string, NavigationEntry> Available() {
      var available = new Dictionary<string, NavigationEntry>(StringComparer.Ordinal);

      foreach (var module in _registry.All) {
        // Withheld is keyed by the server's module id, which is what an administrator governs;
        // attachment is keyed by the client's module name. They differ (Auth vs account), so the two
        // questions are asked with the identifier each one is actually filed under.
        var withheld = !module.Descriptor.IsRequired
            && _registry.IsWithheld(module.Descriptor.AssignmentId ?? module.Name);

        // Detached is asked FIRST and independently of withholding. The two are different
        // questions — one is the user's preference about their own composition, the other is an
        // administrator's decision — and gating "attached?" on "not withheld" makes a detached
        // module reappear, disabled, the moment an administrator withholds it.
        // See docs/adr/0015-module-attachment-is-not-a-security-boundary.md
        if (!module.Descriptor.IsRequired && !_registry.IsAttached(module.Name)) {
          continue;
        }

        foreach (var item in module.GetNavigationItems()) {
          available[KeyOf(item)] = new NavigationEntry(item, !withheld);
        }
      }

      foreach (var item in _hostItems) {
        // TryAdd, not an assignment. Host items are added last, so an assignment let a host item
        // sharing an id with a module's destination silently overwrite it — one of the two pages
        // stopped appearing, with nothing anywhere saying why. First one wins now, which is at
        // least deterministic; the composition itself is checked by a test, because two things
        // claiming one id is a mistake to catch at build time rather than to log at runtime.
        _ = available.TryAdd(KeyOf(item), new NavigationEntry(item, true));
      }
      return available;
    }

    /// <summary>
    /// Adds the destination a placement names, if this build has it.
    /// </summary>
    /// <remarks>
    /// The label and icon come from the deployment, so renaming a heading or a screen is somebody's
    /// afternoon rather than a release. The glyph does not: the deployment sends a semantic name and
    /// this client decides what it looks like, because which code point draws a note is a fact about
    /// the font this build ships with.
    /// <para>
    /// Enabled is the conjunction of both answers. The server's says whether the account may use it;
    /// this client's says whether an administrator withheld the whole module. Either one being false
    /// is a reason not to offer a working row.
    /// </para>
    /// </remarks>
    private void Place(
        List<NavigationEntry> entries,
        IReadOnlyDictionary<string, NavigationEntry> available,
        NavigationPlacement placement,
        string group) {
      if (!available.TryGetValue(placement.Id, out var local)) {
        // The deployment arranged something this build does not have — a module removed since, or a
        // server one version ahead. Skipping it is the only honest option: there is no page to open.
        return;
      }

      entries.Add(new NavigationEntry(
          local.Item with {
            Title = placement.Title.Length == 0 ? local.Item.Title : placement.Title,
            IconGlyph = NavigationIcons.Resolve(placement.Icon, local.Item.IconGlyph),
            Group = group,
          },
          placement.IsEnabled && local.IsEnabled));
    }

    private static string KeyOf(NavigationItem item) {
      return item.Id.Length > 0 ? item.Id : item.PageType.FullName ?? item.Title;
    }

    private bool Permits(NavigationItem item) {
      return item.RequiredPermission.Length == 0 || _permissions.Allows(item.RequiredPermission);
    }
  }
}
