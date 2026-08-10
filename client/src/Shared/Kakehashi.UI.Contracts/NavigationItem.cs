using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Kakehashi.UI.Contracts {
  /// <summary>Where the shell places a navigation item.</summary>
  public enum NavigationItemPlacement {
    /// <summary>The main menu area of the navigation pane.</summary>
    Menu,

    /// <summary>The footer area of the navigation pane, next to the built-in Settings item.</summary>
    Footer,
  }

  /// <summary>A navigation destination a module contributes to the host shell.</summary>
  /// <param name="Title">The label shown in the navigation menu.</param>
  /// <param name="IconGlyph">A Segoe Fluent Icons glyph, for example <c>&#xE721;</c>.</param>
  /// <param name="PageType">The page navigated to when the item is selected.</param>
  /// <param name="Placement">Where the shell places the item; defaults to the main menu.</param>
  public sealed record NavigationItem(
      string Title,
      string IconGlyph,
      Type PageType,
      NavigationItemPlacement Placement = NavigationItemPlacement.Menu) {
    /// <summary>
    /// The stable id the deployment files this destination under — "notes", "account.users".
    /// </summary>
    /// <remarks>
    /// It is what joins a compiled page to the row that says where it sits, so it has to match the
    /// destination the server declares. Empty means the deployment has no say over this item: it is
    /// drawn where the client puts it, which is right for the footer avatar and wrong for anything
    /// in the menu.
    /// <para>
    /// A destination id rather than a module id, because one module can own several destinations —
    /// the account module owns both the account screen and the user directory, and they belong under
    /// different headings.
    /// </para>
    /// </remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The heading this destination sits under when the deployment has not been asked.
    /// </summary>
    /// <remarks>
    /// A fallback, not the answer. Where a destination sits is the deployment's decision and arrives
    /// from the server; this is what the pane falls back to when that call has not happened or could
    /// not be made, so an unreachable server costs the arrangement rather than the whole menu.
    /// </remarks>
    public string Group { get; init; } = string.Empty;

    /// <summary>
    /// The permission an account needs before this destination is usable. Empty means anyone.
    /// </summary>
    /// <remarks>
    /// An item the account cannot use is shown disabled rather than hidden, for the reason the
    /// server answers 403 rather than 404: the destination is compiled into the client they are
    /// running, so hiding it buys nothing and costs the one thing that makes the refusal
    /// actionable — being able to tell "not for you" from "not here", and ask for it.
    /// </remarks>
    public string RequiredPermission { get; init; } = string.Empty;

    /// <summary>
    /// Optional factory for custom item content (e.g. an avatar). When set, the shell uses the
    /// produced element instead of the default icon + title row.
    /// </summary>
    public Func<UIElement>? ContentFactory { get; init; }

    /// <summary>
    /// Optional factory for a flyout. When set, invoking the item shows the flyout anchored to the
    /// item instead of navigating to <see cref="PageType"/> (the page stays reachable through the
    /// flyout's own actions).
    /// </summary>
    public Func<FlyoutBase>? FlyoutFactory { get; init; }
  }
}
