using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.App.Services;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Kakehashi.App.UI {
  public sealed partial class ShellPage : Page {
    private const string _settingsPageKey = "Settings";
    private const string _homePageKey = "Home";
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly Dictionary<string, NavigationViewItem> _navItemsByKey = new(StringComparer.Ordinal);
    private readonly List<NavigationViewItem> _moduleNavItems = [];
    private readonly Dictionary<string, NavigationViewItemHeader> _groupHeaders =
        new(StringComparer.Ordinal);
    private readonly NavigationPlanner _navigationPlanner;
    private readonly INavigationLayoutService _layout;
    private readonly ISubscription _subscription;
    private bool _isSyncingSelection;
    private string? _currentPageKey;

    public ShellPage(
        ShellViewModel viewModel,
        INavigationService navigationService,
        INotificationService notificationService,
        IModuleRegistry moduleRegistry,
        ISubscription subscription,
        IPermissionService permissions,
        INavigationLayoutService layout) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ArgumentNullException.ThrowIfNull(navigationService);
      ArgumentNullException.ThrowIfNull(notificationService);
      ArgumentNullException.ThrowIfNull(moduleRegistry);
      ArgumentNullException.ThrowIfNull(subscription);
      ArgumentNullException.ThrowIfNull(permissions);
      ArgumentNullException.ThrowIfNull(layout);

      ViewModel = viewModel;
      _navigationService = navigationService;
      _notificationService = notificationService;
      _navigationPlanner = new NavigationPlanner(moduleRegistry, permissions);
      _layout = layout;
      _subscription = subscription;

      InitializeComponent();

      // Rebuilt when the deployment's arrangement arrives, which is normally once at startup — but
      // also every time an administrator saves the layout screen, and after signing in as somebody
      // else. The pane is drawn from whatever is current, so the handler needs no argument.
      _layout.Changed += OnLayoutChanged;

      WeakReferenceMessenger.Default.Register<ShellPage, ModuleSetChangedMessage>(
          this, static (page, message) => page.DispatcherQueue.TryEnqueue(
              page.OnModuleSetChanged));
    }

    public ShellViewModel ViewModel { get; }

    // The custom title bar, handed to the window so it becomes the draggable caption.
    public TitleBar TitleBar => AppTitleBar;

    // The in-app notification bar, bound to the notification service.
    public InfoBar NotificationBar => AppNotificationBar;

    private void OnShellPageLoaded(object sender, RoutedEventArgs e) {
      _navigationService.Initialize(NavFrame);
      _navigationService.Register(typeof(HomePage), typeof(SettingsPage));
      foreach (var item in HostNavigation.Items) {
        _navigationService.Register(item.PageType);
      }
      _notificationService.Initialize(AppNotificationBar);

      _navItemsByKey[_homePageKey] = Home;
      RebuildModuleNavItems();

      // Keep the pane selection in sync with whatever the navigation service shows - including
      // back navigation and programmatic navigation, not just pane clicks.
      _subscription.Add(_navigationService.OnNavigated.Subscribe(OnNavigated));

      NavView.SelectedItem = Home;
    }

    // Rebuilds every pane item the shell does not own outright, grouped under their headings.
    //
    // Host items and module items go through the same planner, which is the point: grouping,
    // ordering and gating are decided once instead of once per source. Home stays in XAML because it
    // is the one destination that is always there and always first.
    //
    // What to draw is NavigationPlanner's decision, not this method's. Everything here
    // is about controls: making a NavigationViewItem, giving a disabled one a tooltip that
    // says why, and putting each under its heading.
    private void OnLayoutChanged(object? sender, EventArgs e) {
      _ = DispatcherQueue.TryEnqueue(OnModuleSetChanged);
    }

    private void RebuildModuleNavItems() {
      foreach (var existing in _moduleNavItems) {
        NavView.MenuItems.Remove(existing);
        NavView.FooterMenuItems.Remove(existing);
        if (existing.Tag is string existingKey) {
          _navItemsByKey.Remove(existingKey);
        }
      }

      // The headings too. Clearing only the dictionary left the controls in the pane, so signing
      // in again — which rebuilds — stacked a second "Utilities" and "Administration" under the
      // first.
      foreach (var header in _groupHeaders.Values) {
        NavView.MenuItems.Remove(header);
      }

      _moduleNavItems.Clear();
      _groupHeaders.Clear();

      foreach (var (item, isEnabled) in _navigationPlanner.Plan(_layout.Current)) {
        // Tag the item with the same key the navigation service registers the page under, so a
        // selection (or a back-navigation sync) maps straight to a NavigateTo that resolves.
        string pageKey = _navigationService.GetPageKey(item.PageType);
        var navItem = new NavigationViewItem { Tag = pageKey };

        // The icon is set even for custom content: a non-null Icon keeps the presenter's icon
        // column at its full width in every pane state (expanded and compact), which custom
        // content relies on for stable alignment. Custom content draws its own visual, though,
        // so it gets a blank glyph instead of one showing through from underneath.
        navItem.Icon = new FontIcon {
          Glyph = item.ContentFactory is null ? item.IconGlyph : string.Empty,
        };
        navItem.Content = item.ContentFactory is { } contentFactory
            ? contentFactory()
            : item.Title;

        // Named explicitly rather than left to the presenter. An item drawing custom content has no
        // text for UIA to derive a name from, so the account avatar announced itself as
        // "NavigationViewItem" — the class name — to every screen reader.
        AutomationProperties.SetName(navItem, item.Title);

        if (item.FlyoutFactory is { } flyoutFactory) {
          // Flyout items present transient UI on invoke instead of navigating.
          navItem.SelectsOnInvoked = false;
          FlyoutBase.SetAttachedFlyout(navItem, flyoutFactory());
        }

        navItem.IsEnabled = isEnabled;
        if (!isEnabled) {
          ToolTipService.SetToolTip(navItem, "You do not have access to this.");
        }

        _moduleNavItems.Add(navItem);
        _navItemsByKey[pageKey] = navItem;
        if (item.Placement == NavigationItemPlacement.Footer) {
          NavView.FooterMenuItems.Add(navItem);
        } else {
          AddToGroup(item.Group, navItem);
        }
      }
    }

    // Places an item under its group heading, creating the heading the first time it is needed.
    //
    // Appending rather than inserting at a computed index: the first item of a group creates its
    // heading at the end of the pane, and every later item of that group goes directly after the
    // ones already there. Groups therefore appear in the order they are first contributed, which
    // is the composition root's module order.
    private void AddToGroup(string group, NavigationViewItem navItem) {
      if (group.Length == 0) {
        NavView.MenuItems.Add(navItem);
        return;
      }

      if (!_groupHeaders.TryGetValue(group, out var header)) {
        header = new NavigationViewItemHeader { Content = group };
        _groupHeaders[group] = header;
        NavView.MenuItems.Add(header);
        NavView.MenuItems.Add(navItem);
        return;
      }

      // After the last item already under this heading, which is everything up to the next header.
      int index = NavView.MenuItems.IndexOf(header) + 1;
      while (index < NavView.MenuItems.Count
          && NavView.MenuItems[index] is not NavigationViewItemHeader) {
        index++;
      }
      NavView.MenuItems.Insert(index, navItem);
    }

    private void OnModuleSetChanged() {
      RebuildModuleNavItems();

      if (_currentPageKey is not { } currentKey || currentKey == _settingsPageKey) {
        return;
      }

      if (_navItemsByKey.TryGetValue(currentKey, out var container)) {
        // The selected container may have been recreated by the rebuild; re-point the selection.
        SyncSelection(container);
      } else {
        // The page being shown belongs to a module that is no longer attached: leave it, and
        // clear the back stack so its stale entries cannot be navigated back to.
        _navigationService.NavigateTo(_homePageKey);
        _navigationService.ClearBackStack();
      }
    }

    private void OnNavViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) {
      if (args.InvokedItemContainer is NavigationViewItem item
          && FlyoutBase.GetAttachedFlyout(item) is { } flyout) {
        flyout.ShowAt(item);
      }
    }

    private void OnNavViewSelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args) {
      // Ignore selection changes we made ourselves while syncing to a navigation event.
      if (_isSyncingSelection) {
        return;
      }

      if (args.IsSettingsSelected) {
        _navigationService.NavigateTo(_settingsPageKey);
        return;
      }

      if (args.SelectedItem is NavigationViewItem { Tag: string pageKey }) {
        _navigationService.NavigateTo(pageKey);
      }
    }

    private void OnNavigated(NavigationEvent e) {
      _currentPageKey = e.PageKey;
      var container = e.PageKey == _settingsPageKey
          ? NavView.SettingsItem as NavigationViewItem
          : _navItemsByKey.GetValueOrDefault(e.PageKey);
      if (container is null) {
        return;
      }

      SyncSelection(container);
    }

    // Drives the pane selection (and, via its binding, the header) without re-triggering navigation.
    // NavigationView suppresses selection (even programmatic) for SelectsOnInvoked=false items, so
    // flyout items get the suppression lifted just long enough to show the selection indicator.
    private void SyncSelection(NavigationViewItem container) {
      _isSyncingSelection = true;
      bool restoreSuppression = !container.SelectsOnInvoked;
      try {
        container.SelectsOnInvoked = true;
        ViewModel.Selected = container;
        NavView.SelectedItem = container;
      } finally {
        if (restoreSuppression) {
          container.SelectsOnInvoked = false;
        }

        _isSyncingSelection = false;
      }
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args) {
      _navigationService.GoBack();
    }

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args) {
      NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    // Undoes exactly what loading did, so the page survives being shown twice.
    //
    // The version this replaces cleared the tracking lists and left the controls in the pane, and
    // unsubscribed from things it had subscribed to in the CONSTRUCTOR rather than on load — so an
    // Unloaded/Loaded cycle produced a pane with every row twice and no live subscriptions. Setup
    // and teardown are symmetric now: both happen on load and unload, and both name the same things.
    private void OnShellPageUnloaded(object sender, RoutedEventArgs e) {
      _layout.Changed -= OnLayoutChanged;
      if (!_subscription.Unsubscribed) {
        _subscription.Unsubscribe();
      }

      // The controls, not just the lists that track them. Removing only the lists left the pane
      // populated and the next load appended a second copy of everything.
      foreach (var existing in _moduleNavItems) {
        NavView.MenuItems.Remove(existing);
        NavView.FooterMenuItems.Remove(existing);
      }
      foreach (var header in _groupHeaders.Values) {
        NavView.MenuItems.Remove(header);
      }

      _navItemsByKey.Clear();
      _moduleNavItems.Clear();
      _groupHeaders.Clear();
    }
  }
}
