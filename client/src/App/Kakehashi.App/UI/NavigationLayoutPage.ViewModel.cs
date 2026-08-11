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
  /// <summary>
  /// The Navigation screen: where each of this product's destinations sits in the pane.
  /// </summary>
  /// <remarks>
  /// Which destinations there ARE is compiled into the clients and cannot be edited here — every row
  /// shows the permission that protects it precisely because nothing on this screen can change it.
  /// What can be edited is the arrangement: headings, order, labels, icons, and whether a destination
  /// is offered at all.
  /// <para>
  /// Edits are staged and applied together, which reverses how this screen used to work. The old note
  /// argued that each edit was "a single deliberate act… none of them can leave the layout
  /// half-changed, so there is nothing for a transaction to protect", and that was true of a screen
  /// whose only controls were one picker and one switch per row. It is not true of this one: dragging a
  /// screen into another heading renumbers what it landed among, so one gesture is several writes, and
  /// a sequence of single-row calls has no way to fail halfway without leaving the pane half-rearranged.
  /// The reorder defect that motivated this was exactly that — two writes, the second failing, both
  /// rows left sharing a number.
  /// </para>
  /// </remarks>
  public sealed partial class NavigationLayoutViewModel : ViewModel {
    private readonly INavigationAdminService _admin;
    private readonly INavigationLayoutService _layout;
    private readonly IAccessAdminService _access;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly NavigationPlanner _planner;

    /// <summary>What the last read returned, which is what Discard rebuilds from.</summary>
    /// <remarks>
    /// Rebuilding beats asking each node to undo itself. A node can put its own name back, but nothing
    /// on a node knows which heading it used to sit under or in what order — that is a fact about the
    /// tree, and the tree is what a rebuild restores.
    /// </remarks>
    private IReadOnlyList<NavGroupRow> _loadedGroups = [];
    private IReadOnlyList<NavItemRow> _loadedItems = [];

    /// <summary>The order the saved headings were in, for noticing that somebody re-ordered them.</summary>
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

    /// <summary>The headings, in the order the pane draws them, with the unfiled bucket last.</summary>
    public ObservableCollection<NavHeadingNode> Headings { get; } = [];

    /// <summary>The headings a screen can be moved to, "no heading" first.</summary>
    public ObservableCollection<NavHeadingChoice> HeadingChoices { get; } = [];

    /// <summary>
    /// Every icon name this build can draw.
    /// </summary>
    /// <remarks>
    /// All of them, offered as a picker. The mockup showed five chips as suggestions relevant to the
    /// selected screen, and there is nothing to base relevance on: the vocabulary is a flat list of
    /// names with no notion of which suits a page. Offering the whole list is the honest version of
    /// the same control.
    /// </remarks>
    public IReadOnlyList<NavIconChoice> IconChoices { get; }

    /// <summary>The roles the pane can be previewed as, "nobody" first.</summary>
    public ObservableCollection<NavPreviewRole> PreviewRoles { get; } = [];

    /// <summary>What the pane would look like: the staged arrangement, or a role's saved one.</summary>
    public ObservableCollection<NavigationEntry> Preview { get; } = [];

    /// <summary>What the icon search is showing, and out of how many.</summary>
    public string IconSearchHint =>
        IconQuery.Length == 0
            ? $"{SegoeFluentIcons.Count} icons. Type to narrow them."
            : $"{IconMatches.Count} shown of {SegoeFluentIcons.Count}.";

    /// <summary>What the icon search found, for the flyout behind the last swatch.</summary>
    public ObservableCollection<NavIconChoice> IconMatches { get; } = [];

    /// <summary>The read-only facts about the selected screen.</summary>
    public ObservableCollection<NavCodeFact> CodeFacts { get; } = [];

    /// <summary>Every staged change, for the diff.</summary>
    public ObservableCollection<NavChange> Diff { get; } = [];

    /// <summary>What is typed into the icon search.</summary>
    [ObservableProperty]
    public partial string IconQuery { get; set; }

    /// <summary>Whether the pane preview drawer is showing.</summary>
    /// <remarks>
    /// Closed to begin with. The preview answers a question somebody asks now and then - "what will
    /// this look like to them" - so it costs the two editing columns nothing until it is asked.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsPreviewOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsApplying { get; set; }

    /// <summary>
    /// Whether the note about permissions is showing.
    /// </summary>
    /// <remarks>
    /// Dismissible per visit rather than remembered. It answers the question this screen invites —
    /// "am I about to take somebody's access away" — and that question is worth answering again the
    /// next time somebody opens it.
    /// </remarks>
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

    /// <summary>What the preview is currently showing, and whose arrangement it is.</summary>
    [ObservableProperty]
    public partial string PreviewNote { get; set; } = string.Empty;

    public bool HasChanges => ChangedCount > 0;

    public bool HasSelection => SelectedScreen is not null;

    /// <summary>"3 unsaved changes", or the singular.</summary>
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

        // The roles are for the preview picker, and a build without an authorization module has none.
        // A failure here is not the screen failing: the arrangement loaded, and previewing as somebody
        // else is the one thing that stops working.
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

    /// <summary>Adds a heading at the end, named so it can be renamed rather than demanding a name.</summary>
    /// <remarks>
    /// It is staged like everything else: nothing reaches the server until Apply. The identifier is
    /// chosen here rather than derived from the title, which is what the server does when given an
    /// empty one — because a screen dragged into a heading has to name it, and a title-derived
    /// identifier is not knowable until the apply comes back. Deriving it in this client would mean
    /// re-implementing the server's slug rule and keeping the two in step forever.
    /// </remarks>
    [RelayCommand]
    private void NewHeading() {
      var heading = NavHeadingNode.NewHeading(UniqueTitle("New heading"), NextHeadingOrder());
      heading.Id = UniqueHeadingId();

      Watch(heading);
      Headings.Insert(UnfiledIndex(), heading);
      Recount();
    }

    /// <summary>Removes a heading. What was under it becomes unfiled.</summary>
    /// <remarks>
    /// Confirmed only for a heading that exists on the server: one somebody added a moment ago and has
    /// not applied is theirs to take back without being asked.
    /// </remarks>
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

    /// <summary>Throws away every staged edit.</summary>
    [RelayCommand]
    private void Discard() {
      Rebuild();
    }

    /// <summary>Writes the whole arrangement, or none of it.</summary>
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

        // Reloaded rather than assumed: the server derives identifiers for new headings, and somebody
        // else may have applied something between this screen reading and writing.
        await LoadAsync(cancellationToken);
        await _layout.RefreshAsync(CancellationToken.None);
      } finally {
        IsApplying = false;
      }
    }

    /// <summary>Fills the diff, for the dialog the page opens.</summary>
    public void PrepareDiff() {
      Diff.Clear();
      foreach (var (subject, what) in StagedChanges()) {
        Diff.Add(new NavChange(subject, what));
      }
    }

    /// <summary>Moves the selected screen one place earlier under its heading.</summary>
    [RelayCommand]
    private void MoveUp(NavScreenNode? screen) {
      Nudge(screen, -1);
    }

    /// <summary>Moves the selected screen one place later under its heading.</summary>
    [RelayCommand]
    private void MoveDown(NavScreenNode? screen) {
      Nudge(screen, +1);
    }

    /// <summary>Puts a screen back where the code puts it, under the name the code gives it.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ResetScreen() {
      if (SelectedScreen is not { } screen) {
        return;
      }

      screen.Title = string.Empty;
      screen.Icon = string.Empty;
      screen.IsVisible = true;

      // The heading the code declares, if this build still has it. A destination whose default heading
      // a later release removed goes unfiled rather than nowhere.
      var target = Headings.FirstOrDefault(
          heading => !heading.IsUnfiled && heading.Id == screen.DefaultGroup) ?? Unfiled();

      // Placed by the order the code declares rather than appended: "reset" means the arrangement the
      // product shipped, and appending would put it last among screens that were never moved.
      int index = target.Screens
          .TakeWhile(other => !ReferenceEquals(other, screen)
              && other.SavedOrder <= screen.DefaultOrder)
          .Count();
      Move(screen, target, index);
      Recount();
    }

    /// <summary>Sets the selected screen's icon from the picker.</summary>
    [RelayCommand]
    private void PickIcon(NavIconChoice? choice) {
      if (choice is not null && SelectedScreen is { } screen) {
        screen.Icon = choice.Name;
        Recount();
      }
    }

    /// <summary>
    /// Removes a stored row left over from a module this build no longer has.
    /// </summary>
    /// <remarks>
    /// Not staged, unlike everything else here. It is not an arrangement — the row stops existing — and
    /// the server takes it through its own call, so pretending it were part of the apply would mean a
    /// pending bar that promised something Apply does not do.
    /// </remarks>
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

    /// <summary>Moves a screen under a heading, at an index. What drag and drop calls.</summary>
    public void MoveScreen(NavScreenNode screen, NavHeadingNode heading, int index) {
      ArgumentNullException.ThrowIfNull(screen);
      ArgumentNullException.ThrowIfNull(heading);

      Move(screen, heading, index);
      Recount();
    }

    /// <summary>Moves the whole heading to a new position among the headings.</summary>
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

    /// <summary>
    /// Refills the icon search from the whole font.
    /// </summary>
    /// <remarks>
    /// Capped at forty. The catalogue holds around fifteen hundred icons, which is a number to
    /// search rather than a number to scroll.
    /// </remarks>
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

    /// <summary>Rebuilds the tree from the last read, discarding anything staged.</summary>
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

      // Ordered by what the pane orders by. The server lists declared destinations in declaration
      // order and orphans after them, which is not the order they are drawn in — OrderBy is stable, so
      // the server's order survives as the tie-break exactly as the server's own sort intends.
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

    /// <summary>Counts what is staged and says what it is.</summary>
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

    /// <summary>Every staged change, in the order worth reading them.</summary>
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

    /// <summary>The headings to post, with the orders worked out from their positions.</summary>
    private IReadOnlyList<NavGroupSpec> GroupSpecs() {
      var headings = Headings.Where(heading => !heading.IsUnfiled).ToList();

      // Renumbered only when the order actually moved. Renumbering unconditionally would rewrite rows
      // nobody touched — and on a deployment whose stored orders are 5 and 7, every apply would report
      // changes that were nothing but this client's arithmetic.
      bool renumber = headings.Any(heading => heading.IsNew)
          || !headings.Where(heading => !heading.IsNew).Select(heading => heading.Id)
              .SequenceEqual(_savedHeadingOrder, StringComparer.Ordinal);

      return [.. headings.Select((heading, index) => new NavGroupSpec(
          heading.Id, heading.Title, renumber ? (index + 1) * 10 : heading.SortOrder))];
    }

    /// <summary>The screens to post, with the orders worked out per heading.</summary>
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

    /// <summary>Redraws the pane preview.</summary>
    /// <remarks>
    /// Two different answers, and the note says which is on screen. With no role it is the staged
    /// arrangement drawn locally, so it shows unapplied edits. With a role it is the server's answer
    /// for that role, which reflects what is <em>saved</em> — the server has not been told about the
    /// edits, and cannot be until Apply.
    /// </remarks>
    private async Task RefreshPreviewAsync() {
      // No role picked: the arrangement as staged, drawn by the same planner the shell uses. This is
      // the only preview that can show unapplied edits, because it is the only one this client draws.
      if (PreviewRole is null or { IsYourself: true }) {
        Draw(_planner.Plan(StagedLayout()));
        PreviewNote = "Your own pane, including anything not applied yet.";
        return;
      }

      // A role picked - "nobody" included, whose empty id is what the server reads as "no role". The
      // server answers from what is stored, so this cannot show staged edits and says so.
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

    /// <summary>The staged arrangement in the shape the pane's own planner reads.</summary>
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

    /// <summary>The read-only card: what the code owns about the selected screen.</summary>
    /// <remarks>
    /// Route and "declared in" come from this client, not the server. The server has no notion of a
    /// route — it is the key the shell navigates by — and no way to know which file declares a page.
    /// The mockup showed a source path; a page type and the assembly it lives in is the same fact this
    /// client can actually stand behind.
    /// </remarks>
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

    /// <summary>Reparents a screen without raising the count. Callers call Recount once.</summary>
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

    /// <summary>Any staged edit on any node re-counts the whole screen.</summary>
    /// <remarks>
    /// Counting everything rather than adjusting a total. A change to one field can turn another
    /// node's summary into a different sentence — renaming a heading changes what "moved to" says about
    /// every screen under it — so an incremental count would drift from what is on screen.
    /// </remarks>
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

  /// <summary>A role the pane can be previewed as, plus the "yourself" and "nobody" cases.</summary>
  public sealed record NavPreviewRole(string Id, string Name) {
    /// <summary>
    /// The caller's own pane, and the way back from previewing somebody else's.
    /// </summary>
    /// <remarks>
    /// A real item rather than the picker's placeholder. A ComboBox shows its placeholder only while
    /// nothing is selected, so with this absent the first role somebody previewed was the last —
    /// there was nothing left in the list to choose to get their own pane back.
    /// </remarks>
    public static NavPreviewRole Yourself { get; } = new(_yourselfId, "Yourself");

    /// <summary>
    /// Somebody holding no permissions at all.
    /// </summary>
    /// <remarks>
    /// The useful worst case: it answers "would a new colleague see anything". Its id is empty, which
    /// is what the server reads as "no role".
    /// </remarks>
    public static NavPreviewRole Nobody { get; } = new(string.Empty, "Nobody (no permissions)");

    /// <summary>Not an id the server ever sees: it never leaves this class.</summary>
    private const string _yourselfId = "(yourself)";

    /// <summary>Whether this stands for the caller rather than for a role.</summary>
    public bool IsYourself {
      get { return Id == _yourselfId; }
    }

    public override string ToString() {
      return Name;
    }
  }
}
