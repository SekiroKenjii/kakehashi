using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Kakehashi.UI.Contracts {
  public enum NavigationItemPlacement {
    Menu,

    // The navigation pane's footer, next to the built-in Settings item.
    Footer,
  }

  // IconGlyph is a Segoe Fluent Icons code point, not one of the NavigationIcons names: it is the
  // glyph the page was compiled with, and what a deployment's own icon name falls back to.
  public sealed record NavigationItem(
      string Title,
      string IconGlyph,
      Type PageType,
      NavigationItemPlacement Placement = NavigationItemPlacement.Menu) {
    // The stable id a deployment files this destination under - "notes", "account.users". It joins
    // a compiled page to the row that says where it sits, so it has to match the destination the
    // server declares. Empty means the deployment has no say over this item: it is drawn where the
    // client puts it, which is right for the footer avatar and wrong for anything in the menu.
    //
    // A destination id rather than a module id, because one module can own several destinations:
    // the account module owns both the account screen and the user directory, and they belong under
    // different headings.
    public string Id { get; init; } = string.Empty;

    // A fallback, not the answer. Where a destination sits is the deployment's decision and arrives
    // from the server; this is what the pane uses when that call has not happened or could not be
    // made, so an unreachable server costs the arrangement rather than the whole menu.
    public string Group { get; init; } = string.Empty;

    // Empty means anyone. An item the account cannot use is shown disabled rather than hidden, for
    // the reason the server answers 403 rather than 404: the destination is compiled into the
    // client they are running, so hiding it buys nothing and costs the one thing that makes the
    // refusal actionable - being able to tell "not for you" from "not here", and ask for it.
    public string RequiredPermission { get; init; } = string.Empty;

    // When set, the shell draws this instead of the default icon + title row.
    public Func<UIElement>? ContentFactory { get; init; }

    // When set, invoking the item opens the flyout anchored to it and never navigates to PageType,
    // which stays reachable through the flyout's own actions.
    public Func<FlyoutBase>? FlyoutFactory { get; init; }
  }
}
