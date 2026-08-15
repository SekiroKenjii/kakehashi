using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Kakehashi.UI.Contracts;

/// <summary>Where the shell places a navigation item.</summary>
public enum NavigationItemPlacement
{
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
    NavigationItemPlacement Placement = NavigationItemPlacement.Menu)
{
    /// <summary>
    /// The stable id the deployment files this destination under — "notes", "account.users".
    /// </summary>
    /// <remarks>
    /// Joins the compiled page to the server-declared destination, so it must match the id the
    /// server declares. Empty opts the item out of deployment arrangement — right for the footer
    /// avatar, wrong for anything in the menu.
    /// <para>
    /// A destination id rather than a module id: one module can own several destinations under
    /// different headings (the account module owns the account screen and the user directory).
    /// </para>
    /// </remarks>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The heading this destination sits under when the deployment has not been asked.
    /// </summary>
    /// <remarks>
    /// A fallback only: the server-declared arrangement wins once it arrives; the pane uses this
    /// heading when that call has not happened or failed, so an unreachable server costs the
    /// arrangement rather than the whole menu.
    /// </remarks>
    public string Group { get; init; } = string.Empty;

    /// <summary>
    /// The permission an account needs before this destination is usable. Empty means anyone.
    /// </summary>
    /// <remarks>
    /// An item the account cannot use is shown disabled rather than hidden (the server likewise
    /// answers 403, not 404): the destination is compiled into the running client, and a visible
    /// refusal lets the user tell "not for you" from "not here".
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
