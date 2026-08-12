using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.App.Services;
using Kakehashi.UI.Common.Controls;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.UI {
  // Which destinations there ARE is compiled into the clients and cannot be edited here — every row
  // shows the permission that protects it precisely because nothing on this screen can change it.
  // What can be edited is the arrangement: headings, order, labels, icons, and whether a
  // destination is offered at all.
  //
  // Edits are staged and applied together, which reverses how this screen used to work. Single-row
  // writes were safe while the only controls were one picker and one switch per row; they are not
  // safe here, because dragging a screen into another heading renumbers what it landed among, so
  // one gesture is several writes. The reorder defect that motivated this was exactly that — two
  // writes, the second failing, both rows left sharing a number.
  public sealed partial class NavigationLayoutViewModel : ViewModel {
    private readonly INavigationAdminService _admin;
    private readonly INavigationLayoutService _layout;
    private readonly IAccessAdminService _access;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly NavigationPlanner _planner;

    // What Discard rebuilds from. Rebuilding beats asking each node to undo itself: a node can put
    // its own name back, but nothing on a node knows which heading it used to sit under or in what
    // order — that is a fact about the tree, and the tree is what a rebuild restores.
    private IReadOnlyList<NavGroupRow> _loadedGroups = [];
    private IReadOnlyList<NavItemRow> _loadedItems = [];

    // The order the saved headings were in, for noticing that somebody re-ordered them.
    private IReadOnlyList<string> _savedHeadingOrder = [];

    public NavigationLayoutViewModel(
        INavigationAdminService admin,
        INavigationLayoutService layout,
        IAccessAdminService access,
        INotificationService notifications,
        IDialogService dialogs,
        INavigationService navigation,
        IModuleRegistry registry,
        IPermissionService permissions) {
      ArgumentNullException.ThrowIfNull(admin);
      ArgumentNullException.ThrowIfNull(layout);
      ArgumentNullException.ThrowIfNull(access);
      ArgumentNullException.ThrowIfNull(notifications);
      ArgumentNullException.ThrowIfNull(dialogs);
      ArgumentNullException.ThrowIfNull(navigation);
      ArgumentNullException.ThrowIfNull(registry);
      ArgumentNullException.ThrowIfNull(permissions);
      _admin = admin;
      _layout = layout;
      _access = access;
      _notifications = notifications;
      _dialogs = dialogs;
      _navigation = navigation;
      _planner = new NavigationPlanner(registry, permissions);

      IconQuery = string.Empty;

      IconChoices = [.. NavigationIcons.Names.Select(
          name => new NavIconChoice(name, NavigationIcons.Resolve(name, NavigationIcons.Unknown)))];
    }

    // In the order the pane draws them, with the unfiled bucket always last — UnfiledIndex counts
    // on it.
    public ObservableCollection<NavHeadingNode> Headings { get; } = [];

    public ObservableCollection<NavHeadingChoice> HeadingChoices { get; } = [];

    // All of them, offered as a picker. The mockup showed five chips as suggestions relevant to the
    // selected screen, and there is nothing to base relevance on: the vocabulary is a flat list of
    // names with no notion of which suits a page. Offering the whole list is the honest version of
    // the same control.
    public IReadOnlyList<NavIconChoice> IconChoices { get; }

    public ObservableCollection<NavPreviewRole> PreviewRoles { get; } = [];

    public ObservableCollection<NavigationEntry> Preview { get; } = [];

    public string IconSearchHint =>
        IconQuery.Length == 0
            ? $"{SegoeFluentIcons.Count} icons. Type to narrow them."
            : $"{IconMatches.Count} shown of {SegoeFluentIcons.Count}.";

    public ObservableCollection<NavIconChoice> IconMatches { get; } = [];

    public ObservableCollection<NavCodeFact> CodeFacts { get; } = [];

    public ObservableCollection<NavChange> Diff { get; } = [];

    [ObservableProperty]
    public partial string IconQuery { get; set; }

    // Closed to begin with. The preview answers a question somebody asks now and then - "what will
    // this look like to them" - so it costs the two editing columns nothing until it is asked.
    [ObservableProperty]
    public partial bool IsPreviewOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsApplying { get; set; }

    // Dismissible per visit rather than remembered. It answers the question this screen invites —
    // "am I about to take somebody's access away" — and that question is worth answering again the
    // next time somebody opens it.
    [ObservableProperty]
    public partial bool IsNoteOpen { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    public partial int ChangedCount { get; set; }

    [ObservableProperty]
    public partial string ChangeSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(ResetScreenCommand))]
    public partial NavScreenNode? SelectedScreen { get; set; }

    [ObservableProperty]
    public partial NavPreviewRole? PreviewRole { get; set; }

    [ObservableProperty]
    public partial string PreviewNote { get; set; } = string.Empty;

    public bool HasChanges => ChangedCount > 0;

    public bool HasSelection => SelectedScreen is not null;

    public string ChangeCountText => ChangedCount == 1
        ? "1 unsaved change"
        : string.Format(CultureInfo.CurrentCulture, "{0} unsaved changes", ChangedCount);

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

        _loadedGroups = groups.Value;
        _loadedItems = items.Value;
        Rebuild();

        // The roles are for the preview picker, and a build without an authorization module has
        // none. A failure here is not the screen failing: the arrangement loaded, and previewing as
        // somebody else is the one thing that stops working.
        var roles = await _access.ListRolesAsync(cancellationToken);
        PreviewRoles.Clear();
        PreviewRoles.Add(NavPreviewRole.Yourself);
        PreviewRoles.Add(NavPreviewRole.Nobody);
        if (roles.IsSuccess) {
          foreach (var role in roles.Value) {
            PreviewRoles.Add(new NavPreviewRole(role.Id, role.Name));
          }
        }
        PreviewRole = NavPreviewRole.Yourself;
      } finally {
        IsLoading = false;
      }
    }

    // Named on the spot rather than demanding a name up front; it is renamed in place.
    //
    // Staged like everything else: nothing reaches the server until Apply. The identifier is chosen
    // here rather than derived from the title, which is what the server does when given an empty
    // one — because a screen dragged into a heading has to name it, and a title-derived identifier
    // is not knowable until the apply comes back. Deriving it in this client would mean
    // re-implementing the server's slug rule and keeping the two in step forever.
    [RelayCommand]
    private void NewHeading() {
      var heading = NavHeadingNode.NewHeading(UniqueTitle("New heading"), NextHeadingOrder());
      heading.Id = UniqueHeadingId();

      Watch(heading);
      Headings.Insert(UnfiledIndex(), heading);
      Recount();
    }

    // Confirmed only for a heading that exists on the server: one somebody added a moment ago and
    // has not applied is theirs to take back without being asked.
    [RelayCommand]
    private async Task DeleteHeadingAsync(NavHeadingNode? heading) {
      if (heading is null || !heading.CanDelete) {
        return;
      }

      if (!heading.IsNew) {
        int affected = heading.Screens.Count;
        bool confirmed = await _dialogs.ShowConfirmAsync(
            $"Delete {heading.Title}?",
            affected == 0
                ? "It disappears from the pane when you apply."
                : $"It disappears from the pane when you apply, and the {affected} screen(s) under it "
                    + "become unfiled until somebody files them again.",
            "Delete", "Cancel");
        if (!confirmed) {
          return;
        }
      }

      var unfiled = Unfiled();
      foreach (var screen in heading.Screens.ToList()) {
        Move(screen, unfiled, unfiled.Screens.Count);
      }

      Headings.Remove(heading);
      Recount();
    }

    [RelayCommand]
    private void Discard() {
      Rebuild();
    }

    // Writes the whole arrangement, or none of it.
    [RelayCommand]
    private async Task ApplyAsync(CancellationToken cancellationToken) {
      if (!HasChanges) {
        return;
      }

      IsApplying = true;
      try {
        var applied = await _admin.ApplyLayoutAsync(
            GroupSpecs(), ItemSpecs(), cancellationToken);
        if (applied.IsFailure) {
          // Nothing was written — the server validates the whole arrangement first — so what is on
          // screen is still exactly what the person asked for, and throwing it away would make them
          // do it again.
          _notifications.Show(applied.Error.Message, InfoBarSeverity.Error);
          return;
        }

        var outcome = applied.Value;
        _notifications.Show(
            outcome.Total == 0
                ? "Nothing had changed."
                : $"Applied: {outcome.GroupsCreated} heading(s) added, "
                    + $"{outcome.GroupsUpdated} renamed, {outcome.GroupsDeleted} removed, "
                    + $"{outcome.ItemsChanged} screen(s) changed.",
            InfoBarSeverity.Success);

        // Reloaded rather than assumed: the server derives identifiers for new headings, and
        // somebody else may have applied something between this screen reading and writing.
        await LoadAsync(cancellationToken);
        await _layout.RefreshAsync(CancellationToken.None);
      } finally {
        IsApplying = false;
      }
    }

    public void PrepareDiff() {
      Diff.Clear();
      foreach (var (subject, what) in StagedChanges()) {
        Diff.Add(new NavChange(subject, what));
      }
    }

    [RelayCommand]
    private void MoveUp(NavScreenNode? screen) {
      Nudge(screen, -1);
    }

    [RelayCommand]
    private void MoveDown(NavScreenNode? screen) {
      Nudge(screen, +1);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ResetScreen() {
      if (SelectedScreen is not { } screen) {
        return;
      }

      screen.Title = string.Empty;
      screen.Icon = string.Empty;
      screen.IsVisible = true;

      // A destination whose default heading a later release removed goes unfiled rather than
      // nowhere.
      var target = Headings.FirstOrDefault(
          heading => !heading.IsUnfiled && heading.Id == screen.DefaultGroup) ?? Unfiled();

      // Placed by the order the code declares rather than appended: "reset" means the arrangement
      // the product shipped, and appending would put it last among screens that were never moved.
      int index = target.Screens
          .TakeWhile(other => !ReferenceEquals(other, screen)
              && other.SavedOrder <= screen.DefaultOrder)
          .Count();
      Move(screen, target, index);
      Recount();
    }

    [RelayCommand]
    private void PickIcon(NavIconChoice? choice) {
      if (choice is not null && SelectedScreen is { } screen) {
        screen.Icon = choice.Name;
        Recount();
      }
    }

    // Not staged, unlike everything else here. It is not an arrangement — the row stops existing —
    // and the server takes it through its own call, so pretending it were part of the apply would
    // mean a pending bar that promised something Apply does not do.
    [RelayCommand]
    private async Task DeleteOrphanAsync(NavScreenNode? screen) {
      if (screen is not { IsOrphan: true }) {
        return;
      }

      bool confirmed = await _dialogs.ShowConfirmAsync(
          $"Remove {screen.DisplayTitle}?",
          "This row is left over from a module this build no longer has. Removing it takes effect at "
              + "once, and if that module comes back it will be filed wherever the code puts it.",
          "Remove", "Cancel");
      if (!confirmed) {
        return;
      }

      var removed = await _admin.DeleteItemAsync(screen.Id, CancellationToken.None);
      if (removed.IsFailure) {
        _notifications.Show(removed.Error.Message, InfoBarSeverity.Error);
        return;
      }

      _notifications.Show($"{screen.DisplayTitle} removed.", InfoBarSeverity.Success);
      await LoadAsync(CancellationToken.None);
    }

    public void MoveScreen(NavScreenNode screen, NavHeadingNode heading, int index) {
      ArgumentNullException.ThrowIfNull(screen);
      ArgumentNullException.ThrowIfNull(heading);

      Move(screen, heading, index);
      Recount();
    }

    public void MoveHeading(NavHeadingNode heading, int index) {
      ArgumentNullException.ThrowIfNull(heading);
      if (heading.IsUnfiled) {
        return;
      }

      int from = Headings.IndexOf(heading);
      int to = Math.Clamp(index, 0, UnfiledIndex() - 1);
      if (from < 0 || from == to) {
        return;
      }

      Headings.Move(from, to);
      Recount();
    }

    partial void OnSelectedScreenChanged(NavScreenNode? value) {
      RebuildCodeFacts(value);
    }

    // Capped at forty. The catalogue holds around fifteen hundred icons, which is a number to
    // search rather than a number to scroll.
    partial void OnIconQueryChanged(string value) {
      IconMatches.Clear();
      foreach (var (name, glyph) in SegoeFluentIcons.Search(value, 40)) {
        IconMatches.Add(new NavIconChoice(name, glyph));
      }
      OnPropertyChanged(nameof(IconSearchHint));
    }

    partial void OnPreviewRoleChanged(NavPreviewRole? value) {
      _ = RefreshPreviewAsync();
    }

    private void Rebuild() {
      string? selectedId = SelectedScreen?.Id;

      foreach (var heading in Headings) {
        Unwatch(heading);
        foreach (var screen in heading.Screens) {
          screen.PropertyChanged -= OnNodeChanged;
        }
      }
      Headings.Clear();

      var unfiled = NavHeadingNode.Unfiled();
      foreach (var row in _loadedGroups) {
        var heading = new NavHeadingNode(row);
        Watch(heading);
        Headings.Add(heading);
      }
      Headings.Add(unfiled);

      // The server lists declared destinations in declaration order and orphans after them, which
      // is not the order they are drawn in — OrderBy is stable, so the server's order survives as
      // the tie-break exactly as the server's own sort intends.
      foreach (var row in _loadedItems.OrderBy(row => row.SortOrder)) {
        var target = Headings.FirstOrDefault(
            heading => !heading.IsUnfiled && heading.Id == row.GroupId) ?? unfiled;

        var screen = new NavScreenNode(row) { Heading = target, SavedHeading = target };
        screen.PropertyChanged += OnNodeChanged;
        target.Screens.Add(screen);
      }

      foreach (var heading in Headings) {
        for (int i = 0; i < heading.Screens.Count; i++) {
          heading.Screens[i].SavedIndex = i;
        }
        heading.SavedSequence = [.. heading.Screens.Select(screen => screen.Id)];
      }
      _savedHeadingOrder = [.. Headings.Where(h => !h.IsUnfiled).Select(h => h.Id)];

      RebuildChoices();
      SelectedScreen = Headings
          .SelectMany(heading => heading.Screens)
          .FirstOrDefault(screen => screen.Id == selectedId);
      Recount();
    }

    private void RebuildChoices() {
      HeadingChoices.Clear();
      HeadingChoices.Add(new NavHeadingChoice(string.Empty, "No heading"));
      foreach (var heading in Headings.Where(heading => !heading.IsUnfiled)) {
        HeadingChoices.Add(new NavHeadingChoice(heading.Id, heading.Title));
      }
    }

    private void Recount() {
      var changes = StagedChanges();

      ChangedCount = changes.Count;
      OnPropertyChanged(nameof(ChangeCountText));
      ChangeSummary = changes.Count == 0
          ? string.Empty
          : string.Join(" · ", changes.Take(3).Select(change => $"{change.Subject} {change.What}"))
              + (changes.Count > 3
                  ? string.Format(
                      CultureInfo.CurrentCulture, " · and {0} more", changes.Count - 3)
                  : string.Empty);

      RebuildChoices();
      _ = RefreshPreviewAsync();
    }

    private List<NavChange> StagedChanges() {
      var changes = new List<NavChange>();

      foreach (var heading in Headings.Where(heading => !heading.IsUnfiled)) {
        foreach (var what in heading.Changes) {
          changes.Add(new NavChange(heading.Title, what));
        }
      }

      // A fact about the list rather than about any one heading, which is why no node reports it.
      var order = Headings
          .Where(heading => !heading.IsUnfiled && !heading.IsNew)
          .Select(heading => heading.Id)
          .ToList();
      if (!order.SequenceEqual(_savedHeadingOrder, StringComparer.Ordinal)) {
        changes.Add(new NavChange("The headings", "were re-ordered"));
      }

      foreach (var screen in Headings.SelectMany(heading => heading.Screens)) {
        foreach (var what in screen.Changes) {
          changes.Add(new NavChange(screen.DisplayTitle, what));
        }
      }
      return changes;
    }

    private IReadOnlyList<NavGroupSpec> GroupSpecs() {
      var headings = Headings.Where(heading => !heading.IsUnfiled).ToList();

      // Renumbered only when the order actually moved. Renumbering unconditionally would rewrite
      // rows nobody touched — and on a deployment whose stored orders are 5 and 7, every apply
      // would report changes that were nothing but this client's arithmetic.
      bool renumber = headings.Any(heading => heading.IsNew)
          || !headings.Where(heading => !heading.IsNew).Select(heading => heading.Id)
              .SequenceEqual(_savedHeadingOrder, StringComparer.Ordinal);

      return [.. headings.Select((heading, index) => new NavGroupSpec(
          heading.Id, heading.Title, renumber ? (index + 1) * 10 : heading.SortOrder))];
    }

    private IReadOnlyList<NavItemSpec> ItemSpecs() {
      var specs = new List<NavItemSpec>();

      foreach (var heading in Headings) {
        var sequence = heading.Screens.Select(screen => screen.Id).ToList();
        bool renumber = !sequence.SequenceEqual(heading.SavedSequence, StringComparer.Ordinal);

        for (int i = 0; i < heading.Screens.Count; i++) {
          var screen = heading.Screens[i];
          specs.Add(new NavItemSpec(
              screen.Id,
              heading.IsUnfiled ? string.Empty : heading.Id,
              renumber ? (i + 1) * 10 : screen.SavedOrder,
              screen.Title,
              screen.Icon,
              screen.IsVisible));
        }
      }
      return specs;
    }

    // Two different answers, and the note says which is on screen. With no role it is the staged
    // arrangement drawn locally by the same planner the shell uses, so it shows unapplied edits.
    // With a role it is the server's answer for that role, which reflects what is saved — the
    // server has not been told about the edits, and cannot be until Apply.
    private async Task RefreshPreviewAsync() {
      if (PreviewRole is null or { IsYourself: true }) {
        Draw(_planner.Plan(StagedLayout()));
        PreviewNote = "Your own pane, including anything not applied yet.";
        return;
      }

      // "Nobody" included, whose empty id is what the server reads as "no role".
      var previewed = await _admin.PreviewLayoutAsync(PreviewRole.Id, CancellationToken.None);
      if (previewed.IsFailure) {
        _notifications.Show(previewed.Error.Message, InfoBarSeverity.Error);

        // Back to the local preview rather than leaving the box showing somebody else's pane.
        // Assigning re-enters this method through the change hook, which is where the note is put
        // right.
        PreviewRole = NavPreviewRole.Yourself;
        return;
      }

      Draw(_planner.Plan(previewed.Value));
      PreviewNote =
          $"As {PreviewRole.Name} sees it, from what is saved - not from your unapplied edits.";
    }

    private void Draw(IReadOnlyList<NavigationEntry> entries) {
      Preview.Clear();
      foreach (var entry in entries) {
        Preview.Add(entry);
      }
    }

    private NavigationLayout StagedLayout() {
      var unfiled = Unfiled();
      IReadOnlyList<NavigationPlacement> ungrouped = [
        .. unfiled.Screens.Where(screen => screen.IsVisible)
            .Select(screen => new NavigationPlacement(
                screen.Id, screen.Title, screen.Icon, IsEnabled: true)),
      ];

      IReadOnlyList<NavigationGroup> groups = [
        .. Headings
            .Where(heading => !heading.IsUnfiled)
            .Select(heading => new NavigationGroup(
                heading.Title,
                [.. heading.Screens.Where(screen => screen.IsVisible)
                    .Select(screen => new NavigationPlacement(
                        screen.Id, screen.Title, screen.Icon, IsEnabled: true))]))
            .Where(group => group.Items.Count > 0),
      ];

      return new NavigationLayout(ungrouped, groups);
    }

    // Route and "declared in" come from this client, not the server. The server has no notion of a
    // route — it is the key the shell navigates by — and no way to know which file declares a page.
    // The mockup showed a source path; a page type and the assembly it lives in is the same fact
    // this client can actually stand behind.
    private void RebuildCodeFacts(NavScreenNode? screen) {
      CodeFacts.Clear();
      if (screen is null) {
        return;
      }

      CodeFacts.Add(new NavCodeFact("Screen key", screen.Id));
      CodeFacts.Add(new NavCodeFact("Module", screen.ModuleId.Length > 0 ? screen.ModuleId : "—"));
      CodeFacts.Add(new NavCodeFact(
          "Required permission",
          screen.RequiredPermission.Length > 0 ? screen.RequiredPermission : "none"));

      var item = _planner.Find(screen.Id);
      if (item is null) {
        CodeFacts.Add(new NavCodeFact("Route", "not in this build"));
        CodeFacts.Add(new NavCodeFact("Declared in", "not in this build"));
        return;
      }

      CodeFacts.Add(new NavCodeFact("Route", _navigation.GetPageKey(item.PageType)));
      CodeFacts.Add(new NavCodeFact(
          "Declared in",
          $"{item.PageType.FullName} · {item.PageType.Assembly.GetName().Name}"));
    }

    private void Nudge(NavScreenNode? screen, int direction) {
      if (screen?.Heading is not { } heading) {
        return;
      }

      int from = heading.Screens.IndexOf(screen);
      int to = from + direction;
      if (from < 0 || to < 0 || to >= heading.Screens.Count) {
        return;
      }

      heading.Screens.Move(from, to);
      Recount();
    }

    // Reparents a screen without raising the count. Callers call Recount once.
    private static void Move(NavScreenNode screen, NavHeadingNode heading, int index) {
      screen.Heading?.Screens.Remove(screen);
      screen.Heading = heading;
      heading.Screens.Insert(Math.Clamp(index, 0, heading.Screens.Count), screen);
    }

    private void Watch(NavHeadingNode heading) {
      heading.PropertyChanged += OnNodeChanged;
    }

    private void Unwatch(NavHeadingNode heading) {
      heading.PropertyChanged -= OnNodeChanged;
    }

    // Counting everything rather than adjusting a total. A change to one field can turn another
    // node's summary into a different sentence — renaming a heading changes what "moved to" says
    // about every screen under it — so an incremental count would drift from what is on screen.
    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(NavScreenNode.IsModified)
          || e.PropertyName == nameof(NavHeadingNode.IsModified)) {
        return;
      }
      Recount();
    }

    private NavHeadingNode Unfiled() {
      return Headings.First(heading => heading.IsUnfiled);
    }

    private int UnfiledIndex() {
      return Headings.Count - 1;
    }

    private int NextHeadingOrder() {
      var headings = Headings.Where(heading => !heading.IsUnfiled).ToList();
      return headings.Count == 0 ? 10 : headings.Max(heading => heading.SortOrder) + 10;
    }

    private string UniqueTitle(string wanted) {
      if (Headings.All(heading => heading.Title != wanted)) {
        return wanted;
      }

      // Titles are unique in the database, so a second "New heading" would come back as a conflict
      // rather than as a heading. Numbering it here turns that into nothing anybody has to see.
      for (int suffix = 2; ; suffix++) {
        string candidate = string.Format(
            CultureInfo.CurrentCulture, "{0} {1}", wanted, suffix);
        if (Headings.All(heading => heading.Title != candidate)) {
          return candidate;
        }
      }
    }

    private string UniqueHeadingId() {
      for (int suffix = 1; ; suffix++) {
        string candidate = string.Format(CultureInfo.InvariantCulture, "heading-{0}", suffix);
        if (Headings.All(heading => heading.Id != candidate)) {
          return candidate;
        }
      }
    }
  }

  public sealed record NavPreviewRole(string Id, string Name) {
    // A real item rather than the picker's placeholder. A ComboBox shows its placeholder only while
    // nothing is selected, so with this absent the first role somebody previewed was the last —
    // there was nothing left in the list to choose to get their own pane back.
    public static NavPreviewRole Yourself { get; } = new(_yourselfId, "Yourself");

    // The useful worst case: it answers "would a new colleague see anything". Its id is empty,
    // which is what the server reads as "no role".
    public static NavPreviewRole Nobody { get; } = new(string.Empty, "Nobody (no permissions)");

    // Not an id the server ever sees: it never leaves this class.
    private const string _yourselfId = "(yourself)";

    public bool IsYourself {
      get { return Id == _yourselfId; }
    }

    public override string ToString() {
      return Name;
    }
  }
}
