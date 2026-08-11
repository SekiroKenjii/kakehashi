using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.App.Services;
using Kakehashi.UI.Common.Controls;

namespace Kakehashi.App.UI {
  /// <summary>A heading as the placement picker offers it, including "no heading".</summary>
  public sealed record NavHeadingChoice(string Id, string Title);

  /// <summary>One name in the icon vocabulary, with the glyph it draws.</summary>
  public sealed record NavIconChoice(string Name, string Glyph);

  /// <summary>One read-only line in the "owned by code" card.</summary>
  public sealed record NavCodeFact(string Label, string Value);

  /// <summary>One line of the diff: what a screen or heading was, and what it would become.</summary>
  public sealed record NavChange(string Subject, string What);

  /// <summary>
  /// One destination in the structure tree, as an administrator stages it.
  /// </summary>
  /// <remarks>
  /// Every edit is held here until Apply, which is a reversal of how this screen used to work. What
  /// changed is the gesture: dragging a screen into another heading renumbers what it landed among, so
  /// one action is now several writes — and a sequence of single-row calls cannot fail halfway without
  /// leaving the pane half-rearranged. The staging is what a transaction on the server can then protect.
  /// <para>
  /// The baselines are what "unsaved" is measured against. They are also why the order comparison is
  /// positional rather than numeric: a deployment whose stored orders are 5 and 7 is perfectly
  /// arranged, and a screen that decided "modified" by comparing them against a freshly renumbered
  /// 10 and 20 would claim unsaved changes the moment it opened.
  /// </para>
  /// </remarks>
  public sealed partial class NavScreenNode : ObservableObject {
    private readonly string _savedTitle;
    private readonly string _savedIcon;
    private readonly bool _savedVisible;

    public NavScreenNode(NavItemRow row) {
      ArgumentNullException.ThrowIfNull(row);

      Id = row.Id;
      ModuleId = row.ModuleId;
      DefaultTitle = row.DefaultTitle;
      DefaultIcon = row.DefaultIcon;
      DefaultGroup = row.DefaultGroup;
      DefaultOrder = row.DefaultOrder;
      RequiredPermission = row.RequiredPermission;
      HideWhenDenied = row.HideWhenDenied;
      IsOrphan = row.IsOrphan;

      SavedGroupId = row.GroupId;
      SavedOrder = row.SortOrder;

      Title = row.Title;
      Icon = row.Icon;
      IsVisible = row.IsVisible;

      _savedTitle = row.Title;
      _savedIcon = row.Icon;
      _savedVisible = row.IsVisible;
    }

    public string Id { get; }
    public string ModuleId { get; }

    /// <summary>What the code calls it, shown as the placeholder and offered by Reset.</summary>
    public string DefaultTitle { get; }

    public string DefaultIcon { get; }

    /// <summary>Where the code puts it. What Reset moves it back to.</summary>
    public string DefaultGroup { get; }

    public int DefaultOrder { get; }

    /// <summary>
    /// What the code enforces, and the reason the rest of this screen is safe to edit.
    /// </summary>
    /// <remarks>
    /// On screen precisely because nothing here can change it: somebody wondering why a screen is
    /// invisible to a colleague needs to see what governs it.
    /// </remarks>
    public string RequiredPermission { get; }

    /// <summary>
    /// Whether this screen refuses to be hidden.
    /// </summary>
    /// <remarks>
    /// The server refuses it, with a sentence. Reading it here as well is what stops the switch being
    /// offered at all — three of the five rows on this screen used to carry a switch the server always
    /// rejected, and the only way to find out was to try it and read the error bar.
    /// </remarks>
    public bool HideWhenDenied { get; }

    /// <summary>A stored row whose destination this build no longer has.</summary>
    public bool IsOrphan { get; }

    /// <summary>The heading holding this screen, or null while it is being moved.</summary>
    public NavHeadingNode? Heading { get; internal set; }

    public string SavedGroupId { get; }
    public int SavedOrder { get; }

    /// <summary>
    /// The heading this screen was under when the screen was last read.
    /// </summary>
    /// <remarks>
    /// A reference rather than an identifier, and that is not fussiness. A heading created on screen has
    /// no identifier until the apply comes back, so comparing ids would read "moved from unfiled into a
    /// brand-new heading" as no change at all — both sides being empty — and the pending bar would miss
    /// it entirely.
    /// </remarks>
    public NavHeadingNode? SavedHeading { get; internal set; }

    /// <summary>Where it sat under its heading when the screen was last read, counted from zero.</summary>
    public int SavedIndex { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(TitleHint))]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(IconName))]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(IsIconKnown))]
    [NotifyPropertyChangedFor(nameof(IsIconUnknown))]
    public partial string Icon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(IsHidden))]
    public partial bool IsVisible { get; set; }

    /// <summary>What the pane will show: the override, or the code's own label.</summary>
    public string DisplayTitle => Title.Length > 0 ? Title : DefaultTitle;

    /// <summary>The icon name in force: the override, or the one the code declared.</summary>
    /// <remarks>
    /// Shown rather than typed. The name is chosen from the vocabulary below the field, so a free
    /// text box only ever produced a name this build cannot draw.
    /// </remarks>
    public string IconName => Icon.Length > 0 ? Icon : DefaultIcon;

    /// <summary>The glyph the pane will draw, falling back to the one the page ships with.</summary>
    public string Glyph =>
        NavigationIcons.Resolve(Icon, NavigationIcons.Resolve(DefaultIcon, NavigationIcons.Unknown));

    /// <summary>Whether this build can draw the icon name as typed. Empty is not unknown — it means
    /// "use the code's".</summary>
    public bool IsIconKnown => Icon.Length == 0 || NavigationIcons.Knows(Icon);

    public bool IsIconUnknown => !IsIconKnown;

    /// <summary>Why the label differs from the code's, when it does.</summary>
    public string TitleHint => Title.Length == 0
        ? "Using the name the code gives it."
        : $"Changed — the code calls it \"{DefaultTitle}\".";

    public bool IsHidden => !IsVisible;

    /// <summary>Whether the visibility switch may be offered at all.</summary>
    public bool CanHide => !HideWhenDenied;

    /// <summary>What the row says about itself under the label.</summary>
    public string Subtitle => IsOrphan
        ? $"{Id} · not in this build"
        : $"{Id} · {ModuleId}";

    /// <summary>Whether anything about this screen is staged and unsaved.</summary>
    public bool IsModified {
      get {
        if (Title != _savedTitle || Icon != _savedIcon || IsVisible != _savedVisible) {
          return true;
        }
        if (!ReferenceEquals(Heading, SavedHeading)) {
          return true;
        }
        return Index != SavedIndex;
      }
    }

    /// <summary>Where it sits under its heading now, counted from zero. -1 while unparented.</summary>
    public int Index => Heading?.Screens.IndexOf(this) ?? -1;

    /// <summary>"2 of 5", for the editor's position control.</summary>
    public string PositionText {
      get {
        if (Heading is not { } heading) {
          return string.Empty;
        }
        return string.Format(
            CultureInfo.CurrentCulture, "{0} of {1}",
            heading.Screens.IndexOf(this) + 1, heading.Screens.Count);
      }
    }

    /// <summary>How this screen's staged state differs from what is saved, in words.</summary>
    /// <remarks>
    /// One sentence per kind of change rather than one per field, because that is what the pending bar
    /// and the diff both read. Ordered so the most consequential comes first: hiding a screen is the
    /// only change here that takes something away from somebody.
    /// </remarks>
    public IReadOnlyList<string> Changes {
      get {
        var changes = new List<string>(4);
        if (IsVisible != _savedVisible) {
          changes.Add(IsVisible ? "shown" : "hidden");
        }
        if (!ReferenceEquals(Heading, SavedHeading)) {
          changes.Add(Heading is { IsUnfiled: false } heading
              ? $"moved to {heading.Title}"
              : "unfiled");
        } else if (Index != SavedIndex) {
          changes.Add("reordered");
        }
        if (Title != _savedTitle) {
          changes.Add(Title.Length == 0 ? "name reset" : $"renamed to {Title}");
        }
        if (Icon != _savedIcon) {
          changes.Add(Icon.Length == 0 ? "icon reset" : $"icon set to {Icon}");
        }
        return changes;
      }
    }

    /// <summary>Puts every staged edit back to what is saved.</summary>
    public void Discard() {
      Title = _savedTitle;
      Icon = _savedIcon;
      IsVisible = _savedVisible;
    }

    /// <summary>Re-announces what depends on where this screen sits.</summary>
    /// <remarks>
    /// Position and IsModified are read from the parent's collection, which raises nothing when its
    /// contents move. The tree calls this after a move rather than the node watching its own parent:
    /// a node that subscribed to its parent would have to unsubscribe on every reparent, and a missed
    /// unsubscribe is a row that updates when a different heading changes.
    /// </remarks>
    public void PlacementChanged() {
      OnPropertyChanged(nameof(Index));
      OnPropertyChanged(nameof(PositionText));
      OnPropertyChanged(nameof(IsModified));
    }
  }

  /// <summary>
  /// One heading in the structure tree, holding the screens under it.
  /// </summary>
  /// <remarks>
  /// A heading with an empty id has been created on screen and not applied yet: the server derives the
  /// identifier from the title, so this client cannot know it until the apply comes back.
  /// </remarks>
  public sealed partial class NavHeadingNode : ObservableObject {
    private readonly string _savedTitle;

    public NavHeadingNode(NavGroupRow row) {
      ArgumentNullException.ThrowIfNull(row);
      Id = row.Id;
      IsSystem = row.IsSystem;
      SortOrder = row.SortOrder;
      Title = row.Title;
      _savedTitle = row.Title;
      Screens.CollectionChanged += (_, _) => ScreensChanged();
    }

    private NavHeadingNode(string title, int order, bool unfiled) {
      Id = string.Empty;
      IsSystem = false;
      IsUnfiled = unfiled;
      IsNew = !unfiled;
      SortOrder = order;
      Title = title;
      _savedTitle = unfiled ? title : string.Empty;
      Screens.CollectionChanged += (_, _) => ScreensChanged();
    }

    /// <summary>Builds a heading somebody just added, which has no identifier until it is applied.</summary>
    public static NavHeadingNode NewHeading(string title, int order) {
      return new NavHeadingNode(title, order, unfiled: false);
    }

    /// <summary>
    /// Builds the bucket for screens under no heading at all.
    /// </summary>
    /// <remarks>
    /// Not in the mockup, and necessary anyway. Two kinds of row have no heading: a destination nobody
    /// has filed, and a leftover from a module this build no longer has — which the server lists
    /// deliberately, because this screen is the only place anybody can discover it exists. Without a
    /// bucket they would be invisible on the one screen that manages placement.
    /// <para>
    /// It is not a heading. It has no identifier, it is never sent to the server, it cannot be renamed
    /// or deleted, and it draws itself as what it is.
    /// </para>
    /// </remarks>
    public static NavHeadingNode Unfiled() {
      return new NavHeadingNode("Not in any heading", int.MaxValue, unfiled: true);
    }

    public string Id { get; internal set; }

    public bool IsSystem { get; }

    /// <summary>The bucket for screens under no heading. See <see cref="Unfiled"/>.</summary>
    public bool IsUnfiled { get; }

    /// <summary>Created on screen and not applied yet.</summary>
    public bool IsNew { get; }

    public ObservableCollection<NavScreenNode> Screens { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    public partial string Title { get; set; }

    /// <summary>
    /// The number the pane orders by.
    /// </summary>
    /// <remarks>
    /// Not part of IsModified, and not edited directly any more. Dragging a heading changes its
    /// position in the list, not this number: the number is worked out from the position at apply time,
    /// and only for the headings whose order actually moved. Comparing it here would have missed a drag
    /// entirely, because at the moment of the drag it has not changed yet.
    /// </remarks>
    public int SortOrder { get; internal set; }

    /// <summary>
    /// Whether this heading ships with the product.
    /// </summary>
    /// <remarks>
    /// Renamable and re-orderable, never deletable. The server refuses it too; this only keeps the
    /// button from being offered, because a control that exists to be refused teaches somebody to
    /// distrust the screen.
    /// </remarks>
    public bool CanDelete => !IsSystem && !IsUnfiled;

    /// <summary>Whether it can be renamed. The unfiled bucket is not a heading and has no name.</summary>
    public bool CanRename => !IsUnfiled;

    public bool IsBuiltIn => IsSystem;

    public string CountText => Screens.Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Whether to draw the drop target instead of a list.</summary>
    public bool IsEmpty => Screens.Count == 0;

    /// <summary>
    /// Whether this heading itself is staged and unsaved.
    /// </summary>
    /// <remarks>
    /// Its own name and existence only. Whether the headings have been re-ordered is a fact about the
    /// list rather than about any one of them, so the screen tracks that and this does not.
    /// </remarks>
    public bool IsModified => IsNew || Title != _savedTitle;

    /// <summary>How this heading differs from what is saved, in words.</summary>
    public IReadOnlyList<string> Changes {
      get {
        if (IsUnfiled) {
          return [];
        }
        if (IsNew) {
          return ["added"];
        }

        return Title != _savedTitle ? [$"renamed to {Title}"] : [];
      }
    }

    /// <summary>The saved order of the screens under it, for deciding whether it needs renumbering.</summary>
    public IReadOnlyList<string> SavedSequence { get; internal set; } = [];

    /// <summary>Puts the name back to what is saved.</summary>
    public void Discard() {
      Title = _savedTitle;
    }

    private void ScreensChanged() {
      OnPropertyChanged(nameof(CountText));
      OnPropertyChanged(nameof(IsEmpty));
      foreach (var screen in Screens) {
        screen.PlacementChanged();
      }
    }
  }
}
