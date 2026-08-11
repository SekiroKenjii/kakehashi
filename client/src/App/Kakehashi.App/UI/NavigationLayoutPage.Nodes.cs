using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.App.Services;
using Kakehashi.UI.Common.Controls;

namespace Kakehashi.App.UI {
  // A heading as the placement picker offers it, including "no heading".
  public sealed record NavHeadingChoice(string Id, string Title);

  // One name in the icon vocabulary, with the glyph it draws.
  public sealed record NavIconChoice(string Name, string Glyph);

  // One read-only line in the "owned by code" card.
  public sealed record NavCodeFact(string Label, string Value);

  // One line of the diff: what a screen or heading was, and what it would become.
  public sealed record NavChange(string Subject, string What);

  // One destination in the structure tree, as an administrator stages it.
  //
  // Every edit is held here until Apply, which is a reversal of how this screen used to work. What
  // changed is the gesture: dragging a screen into another heading renumbers what it landed among, so
  // one action is now several writes — and a sequence of single-row calls cannot fail halfway without
  // leaving the pane half-rearranged. The staging is what a transaction on the server can then protect.
  //
  // The baselines are what "unsaved" is measured against. They are also why the order comparison is
  // positional rather than numeric: a deployment whose stored orders are 5 and 7 is perfectly
  // arranged, and a screen that decided "modified" by comparing them against a freshly renumbered
  // 10 and 20 would claim unsaved changes the moment it opened.
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

    // What the code calls it, shown as the placeholder and offered by Reset.
    public string DefaultTitle { get; }

    public string DefaultIcon { get; }

    // Where the code puts it. What Reset moves it back to.
    public string DefaultGroup { get; }

    public int DefaultOrder { get; }

    // What the code enforces, and the reason the rest of this screen is safe to edit.
    //
    // On screen precisely because nothing here can change it: somebody wondering why a screen is
    // invisible to a colleague needs to see what governs it.
    public string RequiredPermission { get; }

    // Whether this screen refuses to be hidden.
    //
    // The server refuses it, with a sentence. Reading it here as well is what stops the switch being
    // offered at all — three of the five rows on this screen used to carry a switch the server always
    // rejected, and the only way to find out was to try it and read the error bar.
    public bool HideWhenDenied { get; }

    // A stored row whose destination this build no longer has.
    public bool IsOrphan { get; }

    // The heading holding this screen, or null while it is being moved.
    public NavHeadingNode? Heading { get; internal set; }

    public string SavedGroupId { get; }
    public int SavedOrder { get; }

    // The heading this screen was under when the screen was last read.
    //
    // A reference rather than an identifier, and that is not fussiness. A heading created on screen has
    // no identifier until the apply comes back, so comparing ids would read "moved from unfiled into a
    // brand-new heading" as no change at all — both sides being empty — and the pending bar would miss
    // it entirely.
    public NavHeadingNode? SavedHeading { get; internal set; }

    // Where it sat under its heading when the screen was last read, counted from zero.
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

    // What the pane will show: the override, or the code's own label.
    public string DisplayTitle => Title.Length > 0 ? Title : DefaultTitle;

    // The icon name in force: the override, or the one the code declared.
    //
    // Shown rather than typed. The name is chosen from the vocabulary below the field, so a free
    // text box only ever produced a name this build cannot draw.
    public string IconName => Icon.Length > 0 ? Icon : DefaultIcon;

    // The glyph the pane will draw, falling back to the one the page ships with.
    public string Glyph =>
        NavigationIcons.Resolve(Icon, NavigationIcons.Resolve(DefaultIcon, NavigationIcons.Unknown));

    // Whether this build can draw the icon name as typed. Empty is not unknown — it means
    // "use the code's".
    public bool IsIconKnown => Icon.Length == 0 || NavigationIcons.Knows(Icon);

    public bool IsIconUnknown => !IsIconKnown;

    // Why the label differs from the code's, when it does.
    public string TitleHint => Title.Length == 0
        ? "Using the name the code gives it."
        : $"Changed — the code calls it \"{DefaultTitle}\".";

    public bool IsHidden => !IsVisible;

    // Whether the visibility switch may be offered at all.
    public bool CanHide => !HideWhenDenied;

    // What the row says about itself under the label.
    public string Subtitle => IsOrphan
        ? $"{Id} · not in this build"
        : $"{Id} · {ModuleId}";

    // Whether anything about this screen is staged and unsaved.
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

    // Where it sits under its heading now, counted from zero. -1 while unparented.
    public int Index => Heading?.Screens.IndexOf(this) ?? -1;

    // "2 of 5", for the editor's position control.
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

    // How this screen's staged state differs from what is saved, in words.
    //
    // One sentence per kind of change rather than one per field, because that is what the pending bar
    // and the diff both read. Ordered so the most consequential comes first: hiding a screen is the
    // only change here that takes something away from somebody.
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

    // Puts every staged edit back to what is saved.
    public void Discard() {
      Title = _savedTitle;
      Icon = _savedIcon;
      IsVisible = _savedVisible;
    }

    // Re-announces what depends on where this screen sits.
    //
    // Position and IsModified are read from the parent's collection, which raises nothing when its
    // contents move. The tree calls this after a move rather than the node watching its own parent:
    // a node that subscribed to its parent would have to unsubscribe on every reparent, and a missed
    // unsubscribe is a row that updates when a different heading changes.
    public void PlacementChanged() {
      OnPropertyChanged(nameof(Index));
      OnPropertyChanged(nameof(PositionText));
      OnPropertyChanged(nameof(IsModified));
    }
  }

  // One heading in the structure tree, holding the screens under it.
  //
  // A heading with an empty id has been created on screen and not applied yet: the server derives the
  // identifier from the title, so this client cannot know it until the apply comes back.
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

    // Builds a heading somebody just added, which has no identifier until it is applied.
    public static NavHeadingNode NewHeading(string title, int order) {
      return new NavHeadingNode(title, order, unfiled: false);
    }

    // Builds the bucket for screens under no heading at all.
    //
    // Not in the mockup, and necessary anyway. Two kinds of row have no heading: a destination nobody
    // has filed, and a leftover from a module this build no longer has — which the server lists
    // deliberately, because this screen is the only place anybody can discover it exists. Without a
    // bucket they would be invisible on the one screen that manages placement.
    //
    // It is not a heading. It has no identifier, it is never sent to the server, it cannot be renamed
    // or deleted, and it draws itself as what it is.
    public static NavHeadingNode Unfiled() {
      return new NavHeadingNode("Not in any heading", int.MaxValue, unfiled: true);
    }

    public string Id { get; internal set; }

    public bool IsSystem { get; }

    // The bucket for screens under no heading. See Unfiled.
    public bool IsUnfiled { get; }

    // Created on screen and not applied yet.
    public bool IsNew { get; }

    public ObservableCollection<NavScreenNode> Screens { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    public partial string Title { get; set; }

    // The number the pane orders by.
    //
    // Not part of IsModified, and not edited directly any more. Dragging a heading changes its
    // position in the list, not this number: the number is worked out from the position at apply time,
    // and only for the headings whose order actually moved. Comparing it here would have missed a drag
    // entirely, because at the moment of the drag it has not changed yet.
    public int SortOrder { get; internal set; }

    // Whether this heading ships with the product.
    //
    // Renamable and re-orderable, never deletable. The server refuses it too; this only keeps the
    // button from being offered, because a control that exists to be refused teaches somebody to
    // distrust the screen.
    public bool CanDelete => !IsSystem && !IsUnfiled;

    // Whether it can be renamed. The unfiled bucket is not a heading and has no name.
    public bool CanRename => !IsUnfiled;

    public bool IsBuiltIn => IsSystem;

    public string CountText => Screens.Count.ToString(CultureInfo.CurrentCulture);

    // Whether to draw the drop target instead of a list.
    public bool IsEmpty => Screens.Count == 0;

    // Whether this heading itself is staged and unsaved.
    //
    // Its own name and existence only. Whether the headings have been re-ordered is a fact about the
    // list rather than about any one of them, so the screen tracks that and this does not.
    public bool IsModified => IsNew || Title != _savedTitle;

    // How this heading differs from what is saved, in words.
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

    // The saved order of the screens under it, for deciding whether it needs renumbering.
    public IReadOnlyList<string> SavedSequence { get; internal set; } = [];

    // Puts the name back to what is saved.
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
