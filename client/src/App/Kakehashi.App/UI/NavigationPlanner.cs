using System;
using System.Collections.Generic;
using System.Linq;
using Kakehashi.App.Services;
using Kakehashi.UI.Common.Controls;
using Kakehashi.UI.Contracts;

namespace Kakehashi.App.UI {
  public sealed record NavigationEntry(NavigationItem Item, bool IsEnabled);

  /// <summary>
  /// Decides what goes in the navigation pane, in what order, and what is reachable.
  /// </summary>
  /// <remarks>
  /// Kept off the shell page because a <c>Page</c> cannot be constructed off the UI thread, so a
  /// rule living on one is untestable. The client decides which destinations exist (a destination
  /// is a compiled page); the deployment decides heading, order, label, and whether an account may
  /// use them.
  /// </remarks>
  public sealed class NavigationPlanner {
    private readonly IModuleRegistry _registry;
    private readonly IPermissionService _permissions;
    private readonly IReadOnlyList<NavigationItem> _hostItems;

    public NavigationPlanner(IModuleRegistry registry, IPermissionService permissions)
        : this(registry, permissions, HostNavigation.Items) {
    }

    /// <summary>Takes the host items explicitly so a test can supply its own.</summary>
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
    /// The client skips placements this build has no page for, honours the user's detached
    /// modules, and places the footer items itself. An empty layout — first run, or the server
    /// unreachable — falls back to <see cref="PlanLocally"/>.
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

      // An item with no id was never offered to the deployment for arrangement (the footer items),
      // so it keeps the placement the client gave it.
      foreach (var (item, isEnabled) in available.Values.Where(entry => entry.Item.Id.Length == 0)) {
        entries.Add(new NavigationEntry(item, isEnabled));
      }
      return entries;
    }

    /// <summary>
    /// The pane as this build alone would draw it, used until the deployment's answer arrives.
    /// </summary>
    /// <remarks>
    /// Client-side approximation of the server's rules: a destination whose permission the
    /// account lacks is absent (fail closed); a module an administrator withheld is present but
    /// disabled, so it can be seen and asked for; a module the user detached is absent. The
    /// required module is exempt from withholding — its page holds sign-out and account
    /// management, and an account that cannot reach it is stuck.
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
    /// come from the page type this build compiled, not from the server — the server has no notion of
    /// a route, and no way to know which type declares a page.
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

      // Two questions, own keys, detached asked first:
      // docs/adr/0015-module-attachment-is-not-a-security-boundary.md
      foreach (var module in _registry.All) {
        var withheld = !module.Descriptor.IsRequired
            && _registry.IsWithheld(module.Descriptor.AssignmentId ?? module.Name);

        if (!module.Descriptor.IsRequired && !_registry.IsAttached(module.Name)) {
          continue;
        }

        foreach (var item in module.GetNavigationItems()) {
          available[KeyOf(item)] = new NavigationEntry(item, !withheld);
        }
      }

      foreach (var item in _hostItems) {
        // TryAdd, never an assignment: host items go in last, so assigning would silently
        // overwrite a module destination sharing the id.
        _ = available.TryAdd(KeyOf(item), new NavigationEntry(item, true));
      }
      return available;
    }

    /// <summary>
    /// Adds the destination a placement names, if this build has it.
    /// </summary>
    /// <remarks>
    /// The label and icon name come from the deployment, so renaming needs no client release. The
    /// glyph is resolved by this client: which code point a name draws depends on the font this
    /// build ships with.
    /// <para>
    /// Enabled is the conjunction of both answers: the server's (may the account use it) and this
    /// client's (is the whole module withheld).
    /// </para>
    /// </remarks>
    private void Place(
        List<NavigationEntry> entries,
        IReadOnlyDictionary<string, NavigationEntry> available,
        NavigationPlacement placement,
        string group) {
      if (!available.TryGetValue(placement.Id, out var local)) {
        // A placement this build has no page for — a module absent from this build, or a server a
        // version ahead. There is nothing to open, so it is skipped.
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
