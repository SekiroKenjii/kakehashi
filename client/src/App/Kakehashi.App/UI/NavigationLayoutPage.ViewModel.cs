using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.App.Services;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
  /// <summary>One heading, as the layout screen edits it.</summary>
  public sealed partial class NavHeadingViewModel : ViewModel {
    public NavHeadingViewModel(NavGroupRow row) {
      ArgumentNullException.ThrowIfNull(row);
      Id = row.Id;
      Title = row.Title;
      SortOrder = row.SortOrder;
      IsSystem = row.IsSystem;
    }

    public string Id { get; }

    public bool IsSystem { get; }

    /// <summary>
    /// Whether this heading can be deleted. A system heading cannot.
    /// </summary>
    /// <remarks>
    /// The server refuses it too, with a sentence. This only keeps the button from being offered —
    /// a control that exists to be refused is a control that teaches somebody to distrust the screen.
    /// </remarks>
    public bool CanDelete => !IsSystem;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial int SortOrder { get; set; }

    /// <summary>
    /// The same position, as a double, because <c>NumberBox</c> has no integer value.
    /// </summary>
    /// <remarks>
    /// A property rather than a converter: <c>x:Bind</c> checks a property against the control it is
    /// bound to at compile time, and a converter defers the same mistake to runtime.
    /// </remarks>
    public double Position {
      get => SortOrder;
      set => SortOrder = (int)value;
    }
  }

  /// <summary>One destination, as the layout screen edits it.</summary>
  /// <remarks>
  /// The commands are on the row rather than the page because a <c>DataTemplate</c> binds to the row:
  /// reaching the page's view model from inside one means an ancestor binding, which <c>x:Bind</c>
  /// cannot compile-check. The row calls back into the page's view model, which owns the service.
  /// </remarks>
  public sealed partial class NavDestinationViewModel : ViewModel {
    private readonly NavigationLayoutViewModel _owner;

    // What the server last confirmed. The picker and the switch both raise their change events while
    // their bindings are being applied, so without something to compare against, drawing the list
    // would look exactly like somebody editing every row in it.
    private string _savedGroupId;
    private bool _savedVisible;

    public NavDestinationViewModel(
        NavItemRow row, NavigationLayoutViewModel owner, IReadOnlyList<NavHeadingChoice> headings) {
      ArgumentNullException.ThrowIfNull(row);
      ArgumentNullException.ThrowIfNull(owner);
      ArgumentNullException.ThrowIfNull(headings);
      _owner = owner;
      Headings = headings;

      Id = row.Id;
      ModuleId = row.ModuleId;
      DefaultTitle = row.DefaultTitle;
      DefaultIcon = row.DefaultIcon;
      RequiredPermission = row.RequiredPermission;
      HideWhenDenied = row.HideWhenDenied;
      IsOrphan = row.IsOrphan;

      Title = row.Title;
      Icon = row.Icon;
      GroupId = row.GroupId;
      SortOrder = row.SortOrder;
      IsVisible = row.IsVisible;
      _savedGroupId = row.GroupId;
      _savedVisible = row.IsVisible;
    }

    public string Id { get; }

    public string ModuleId { get; }

    public string DefaultTitle { get; }

    public string DefaultIcon { get; }

    public string RequiredPermission { get; }

    public bool HideWhenDenied { get; }

    /// <summary>A stored row whose destination this build no longer has.</summary>
    public bool IsOrphan { get; }

    /// <summary>The headings this destination can be moved to, for the row's picker.</summary>
    /// <remarks>
    /// On the row because a <c>DataTemplate</c> binds to the row: the page's own collection is not
    /// addressable from inside one without an ancestor binding, which <c>x:Bind</c> cannot
    /// compile-check.
    /// <para>
    /// A snapshot, and every row gets its own. Sharing one observable collection meant that reloading
    /// the screen cleared a collection five pickers were bound to, blanking all five selections - and
    /// a blanked selection is indistinguishable from somebody choosing "no heading". Rows are rebuilt
    /// on every load anyway, so a snapshot costs nothing and cannot be pulled out from under a
    /// control.
    /// </para>
    /// </remarks>
    public IReadOnlyList<NavHeadingChoice> Headings { get; }

    /// <summary>The label an administrator typed, or empty to use what the code calls it.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>A semantic icon name, or empty to use the one the page ships with.</summary>
    [ObservableProperty]
    public partial string Icon { get; set; }

    [ObservableProperty]
    public partial string GroupId { get; set; }

    [ObservableProperty]
    public partial int SortOrder { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>What the pane will actually show: the override, or the code's own label.</summary>
    public string DisplayTitle => Title.Length > 0 ? Title : DefaultTitle;

    /// <summary>
    /// The line under the label: what this destination is, and what protects it.
    /// </summary>
    /// <remarks>
    /// The permission is on screen because it is the one thing here nobody can change, and somebody
    /// wondering why a screen is invisible to a colleague needs to see it to find out.
    /// </remarks>
    public string Subtitle {
      get {
        if (IsOrphan) {
          return $"{Id} · not in this build";
        }
        return RequiredPermission.Length == 0
            ? $"{Id} · {ModuleId}"
            : $"{Id} · {ModuleId} · needs {RequiredPermission}";
      }
    }

    /// <summary>
    /// The heading this destination sits under, as the picker's own item.
    /// </summary>
    /// <remarks>
    /// The setter performs the move, which is the whole reason it exists. The previous version bound
    /// the picker's value and read the row back out of the control's <c>Tag</c> in a
    /// <c>SelectionChanged</c> handler — and on an <c>ItemsRepeater</c> recycling a row, the value
    /// binding lands before the <c>Tag</c> binding does, so the handler ran with the new heading and
    /// the PREVIOUS row and moved a destination nobody had touched. A property setter cannot be
    /// wrong about which row it is on: it is on this one.
    /// <para>
    /// The guard is still needed. A binding settling writes here too, and a picker whose item list
    /// is being replaced briefly writes null — which is not somebody choosing "No heading", because
    /// that is a real item whose id happens to be empty.
    /// </para>
    /// </remarks>
    public NavHeadingChoice? Heading {
      get => Headings.FirstOrDefault(choice => choice.Id == GroupId);
      set {
        if (value is null || !WouldMoveTo(value.Id)) {
          return;
        }
        _ = _owner.MoveAsync(this, value.Id, SortOrder);
      }
    }

    /// <summary>Moves it earlier under its heading.</summary>
    [RelayCommand]
    private Task MoveUpAsync() {
      return _owner.ReorderAsync(this, -1);
    }

    /// <summary>Moves it later under its heading.</summary>
    [RelayCommand]
    private Task MoveDownAsync() {
      return _owner.ReorderAsync(this, +1);
    }

    /// <summary>Saves the label, icon and visibility as typed.</summary>
    [RelayCommand]
    private Task ApplyAsync() {
      return _owner.ApplyAsync(this);
    }

    /// <summary>Clears the overrides, returning the destination to what the code calls it.</summary>
    [RelayCommand]
    private Task ResetAsync() {
      Title = string.Empty;
      Icon = string.Empty;
      return _owner.ApplyAsync(this);
    }

    /// <summary>Whether a picked heading is actually a change, rather than a binding settling.</summary>
    internal bool WouldMoveTo(string groupId) {
      return groupId != _savedGroupId;
    }

    /// <summary>The same question for the visibility switch.</summary>
    internal bool WouldChangeVisibility() {
      return IsVisible != _savedVisible;
    }

    internal void Refresh(NavItemRow row) {
      // The baselines FIRST. They are what the change handlers compare against, and assigning the
      // observable properties raises those handlers — so updating the baselines afterwards let a
      // value the server had just corrected read as a fresh edit and issue the write a second time.
      _savedGroupId = row.GroupId;
      _savedVisible = row.IsVisible;

      Title = row.Title;
      Icon = row.Icon;
      GroupId = row.GroupId;
      SortOrder = row.SortOrder;
      IsVisible = row.IsVisible;
      OnPropertyChanged(nameof(DisplayTitle));
      OnPropertyChanged(nameof(Heading));
    }
  }

  /// <summary>A heading as the group picker offers it, including "no heading".</summary>
  public sealed record NavHeadingChoice(string Id, string Title);

  /// <summary>
  /// The Navigation screen: where each of this product's destinations sits in the pane.
  /// </summary>
  /// <remarks>
  /// This is the screen the whole design exists for. Which destinations there ARE is compiled into
  /// the clients and cannot be edited here — the list is read-only, and every row shows the
  /// permission that protects it precisely because nothing on this screen can change it. What can be
  /// edited is the arrangement: headings, order, labels, and whether a destination is offered.
  /// <para>
  /// Every edit is applied immediately rather than staged behind a save bar. Each one is a single
  /// deliberate act — dropping a screen under a heading, hiding one — and none of them can leave the
  /// layout half-changed, so there is nothing for a transaction to protect. That is the opposite of
  /// the Role Permissions screen, where eight toggles are one decision and a partial save would be a
  /// state nobody asked for.
  /// </para>
  /// </remarks>
  public sealed partial class NavigationLayoutViewModel : ViewModel {
    private readonly INavigationAdminService _admin;
    private readonly INavigationLayoutService _layout;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;

    public NavigationLayoutViewModel(
        INavigationAdminService admin,
        INavigationLayoutService layout,
        INotificationService notifications,
        IDialogService dialogs) {
      ArgumentNullException.ThrowIfNull(admin);
      ArgumentNullException.ThrowIfNull(layout);
      ArgumentNullException.ThrowIfNull(notifications);
      ArgumentNullException.ThrowIfNull(dialogs);
      _admin = admin;
      _layout = layout;
      _notifications = notifications;
      _dialogs = dialogs;

      Headings = [];
      HeadingChoices = [];
      Destinations = [];
    }

    public ObservableCollection<NavHeadingViewModel> Headings { get; }

    /// <summary>The headings a destination can be moved to, "No heading" first.</summary>
    /// <remarks>
    /// Rebuilt on every load and handed to the rows as a snapshot, never bound to directly.
    /// </remarks>
    public List<NavHeadingChoice> HeadingChoices { get; }

    public ObservableCollection<NavDestinationViewModel> Destinations { get; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial NavHeadingViewModel? SelectedHeading { get; set; }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken) {
      IsLoading = true;
      try {
        var groups = await _admin.ListGroupsAsync(cancellationToken);
        if (groups.IsFailure) {
          _notifications.Show(groups.Error.Message, InfoBarSeverity.Error);
          return;
        }

        var items = await _admin.ListItemsAsync(cancellationToken);
        if (items.IsFailure) {
          _notifications.Show(items.Error.Message, InfoBarSeverity.Error);
          return;
        }

        var selectedId = SelectedHeading?.Id;
        Headings.Clear();
        HeadingChoices.Clear();
        HeadingChoices.Add(new NavHeadingChoice(string.Empty, "No heading"));
        foreach (var group in groups.Value) {
          Headings.Add(new NavHeadingViewModel(group));
          HeadingChoices.Add(new NavHeadingChoice(group.Id, group.Title));
        }
        SelectedHeading = Headings.FirstOrDefault(heading => heading.Id == selectedId);

        // A snapshot for the rows, taken once the choices are settled. They must not share the list
        // this method just rebuilt.
        IReadOnlyList<NavHeadingChoice> choices = [.. HeadingChoices];
        Destinations.Clear();
        foreach (var item in items.Value) {
          Destinations.Add(new NavDestinationViewModel(item, this, choices));
        }
      } finally {
        IsLoading = false;
      }
    }

    /// <summary>Adds a heading, named so it can be renamed rather than demanding a name up front.</summary>
    /// <remarks>
    /// The order is the end of the list, so a new heading appears at the bottom of the pane. Anywhere
    /// else and creating one would silently reshuffle what was already there.
    /// </remarks>
    [RelayCommand]
    private async Task NewHeadingAsync() {
      var order = Headings.Count == 0 ? 10 : Headings.Max(heading => heading.SortOrder) + 10;
      var title = UniqueTitle("New heading");

      var created = await _admin.CreateGroupAsync(string.Empty, title, order, CancellationToken.None);
      if (created.IsFailure) {
        _notifications.Show(created.Error.Message, InfoBarSeverity.Error);
        return;
      }

      await ReloadAsync();
      SelectedHeading = Headings.FirstOrDefault(heading => heading.Id == created.Value.Id);
    }

    /// <summary>Saves the selected heading's name and position.</summary>
    [RelayCommand]
    private async Task SaveHeadingAsync() {
      if (SelectedHeading is not { } heading) {
        return;
      }

      var saved = await _admin.UpdateGroupAsync(
          heading.Id, heading.Title, heading.SortOrder, CancellationToken.None);
      if (saved.IsFailure) {
        _notifications.Show(saved.Error.Message, InfoBarSeverity.Error);
        return;
      }

      _notifications.Show($"{saved.Value.Title} saved.", InfoBarSeverity.Success);
      await ReloadAsync();
    }

    /// <summary>
    /// Deletes the selected heading. What was under it falls out of every heading rather than
    /// disappearing with it.
    /// </summary>
    [RelayCommand]
    private async Task DeleteHeadingAsync() {
      if (SelectedHeading is not { } heading) {
        return;
      }

      // Confirmed, because it lands on every client at once and nothing undoes it. The count is in
      // the question: "delete Monitoring" and "unfile the three screens under it" are the same act,
      // and only one of them is obvious from the button.
      var affected = Destinations.Count(d => d.GroupId == heading.Id);
      var confirmed = await _dialogs.ShowConfirmAsync(
          $"Delete {heading.Title}?",
          affected == 0
              ? "The heading disappears from everyone's navigation pane."
              : $"The heading disappears from everyone's navigation pane, and the {affected} "
                  + "screen(s) under it become unfiled until somebody files them again.",
          "Delete", "Cancel");
      if (!confirmed) {
        return;
      }

      var deleted = await _admin.DeleteGroupAsync(heading.Id, CancellationToken.None);
      if (deleted.IsFailure) {
        _notifications.Show(deleted.Error.Message, InfoBarSeverity.Error);
        return;
      }

      _notifications.Show($"{heading.Title} deleted. What was under it is now unfiled.", InfoBarSeverity.Success);
      SelectedHeading = null;
      await ReloadAsync();
    }

    internal async Task MoveAsync(NavDestinationViewModel item, string groupId, int order) {
      var moved = await _admin.MoveItemAsync(item.Id, groupId, order, CancellationToken.None);
      if (moved.IsFailure) {
        _notifications.Show(moved.Error.Message, InfoBarSeverity.Error);
        // Put the row back where the server still has it, so the picker does not show a move that
        // did not happen.
        await ReloadAsync();
        return;
      }

      item.Refresh(moved.Value);
      await RefreshPaneAsync();
    }

    /// <summary>
    /// Moves a destination one place earlier or later under its own heading.
    /// </summary>
    /// <remarks>
    /// It swaps orders with its neighbour rather than renumbering the list. Two writes instead of
    /// one, and worth it: renumbering would rewrite every row under the heading, so a stale screen
    /// could push somebody else's placement around while claiming to move one item.
    /// </remarks>
    internal async Task ReorderAsync(NavDestinationViewModel item, int direction) {
      var siblings = Destinations
          .Where(other => other.GroupId == item.GroupId && !other.IsOrphan)
          .OrderBy(other => other.SortOrder)
          .ThenBy(other => other.Id, StringComparer.Ordinal)
          .ToList();

      // An orphan is not among its own siblings — the list above excludes them — so IndexOf
      // returns -1, and -1 plus one is a perfectly valid index into somebody else's row. "Move
      // down" on a leftover row reordered an unrelated destination; "move up" silently did nothing.
      // The buttons are disabled for orphans now as well; this is the half that cannot be skipped.
      var position = siblings.IndexOf(item);
      if (position < 0) {
        return;
      }

      var index = position + direction;
      if (index < 0 || index >= siblings.Count) {
        return;
      }

      var neighbour = siblings[index];
      var (mine, theirs) = (item.SortOrder, neighbour.SortOrder);
      if (mine == theirs) {
        // Two destinations a deployment gave the same number: nudge one so the swap has an effect.
        theirs = direction < 0 ? mine - 1 : mine + 1;
      }

      var moved = await _admin.MoveItemAsync(
          item.Id, item.GroupId, theirs, CancellationToken.None);
      if (moved.IsFailure) {
        _notifications.Show(moved.Error.Message, InfoBarSeverity.Error);
        return;
      }

      var swapped = await _admin.MoveItemAsync(
          neighbour.Id, neighbour.GroupId, mine, CancellationToken.None);
      if (swapped.IsFailure) {
        _notifications.Show(swapped.Error.Message, InfoBarSeverity.Error);
      }

      await ReloadAsync();
      await RefreshPaneAsync();
    }

    internal async Task ApplyAsync(NavDestinationViewModel item) {
      var saved = await _admin.UpdateItemAsync(
          item.Id, item.Title, item.Icon, item.IsVisible, CancellationToken.None);
      if (saved.IsFailure) {
        _notifications.Show(saved.Error.Message, InfoBarSeverity.Error);
        await ReloadAsync();
        return;
      }

      item.Refresh(saved.Value);
      await RefreshPaneAsync();
    }

    /// <summary>Re-reads the screen after a write.</summary>
    /// <remarks>
    /// It calls the method rather than the command. Re-entering an <c>IAsyncRelayCommand</c> cancels
    /// the execution already running, and the cancelled one surfaced gRPC's own cancellation text as
    /// an error bar and cleared IsLoading while its replacement was still running — a spinner that
    /// stops early and an error about nothing.
    /// </remarks>
    private Task ReloadAsync() {
      return LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Re-reads the pane so the change is visible in the navigation bar immediately.
    /// </summary>
    /// <remarks>
    /// Without this, an administrator arranges the pane and sees nothing happen until they sign in
    /// again — which reads as the screen being broken rather than as a cache being stale.
    /// </remarks>
    private Task RefreshPaneAsync() {
      return _layout.RefreshAsync(CancellationToken.None);
    }

    private string UniqueTitle(string wanted) {
      if (Headings.All(heading => heading.Title != wanted)) {
        return wanted;
      }

      // Titles are unique in the database, so a second "New heading" would come back as a conflict
      // rather than as a heading. Numbering it here turns that into nothing the user has to see.
      for (var suffix = 2; ; suffix++) {
        var candidate = $"{wanted} {suffix}";
        if (Headings.All(heading => heading.Title != candidate)) {
          return candidate;
        }
      }
    }
  }
}
