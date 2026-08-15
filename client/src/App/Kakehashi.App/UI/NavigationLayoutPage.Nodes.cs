using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.App.Services;
using Kakehashi.UI.Common.Controls;

namespace Kakehashi.App.UI;

/// <summary>An empty <c>Id</c> is the real "no heading" choice, not a missing value.</summary>
public sealed record NavHeadingChoice(string Id, string Title);

public sealed record NavIconChoice(string Name, string Glyph);

public sealed record NavCodeFact(string Label, string Value);

public sealed record NavChange(string Subject, string What);

/// <summary>
/// One destination in the structure tree, as an administrator stages it.
/// </summary>
/// <remarks>
/// Edits are held until Apply: docs/adr/0004-staged-edits-atomic-apply.md
/// <para>
/// The order comparison against the baseline is positional, not numeric: stored orders of 5 and
/// 7 are a valid arrangement, and comparing them against renumbered values would claim unsaved
/// changes the moment the screen opened.
/// </para>
/// </remarks>
public sealed partial class NavScreenNode : ObservableObject
{
    private readonly string _savedTitle;
    private readonly string _savedIcon;
    private readonly bool _savedVisible;

    public NavScreenNode(NavItemRow row)
    {
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

    /// <summary>The heading the code declares; Reset moves the screen back to it.</summary>
    public string DefaultGroup { get; }

    public int DefaultOrder { get; }

    /// <summary>
    /// The permission the code enforces; shown on screen because nothing here can change it.
    /// </summary>
    public string RequiredPermission { get; }

    /// <summary>
    /// The server rejects hiding such a screen; reading the flag here keeps the switch from being
    /// offered at all.
    /// </summary>
    public bool HideWhenDenied { get; }

    /// <summary>A stored row whose destination is not part of this build.</summary>
    public bool IsOrphan { get; }

    /// <summary>The heading holding this screen, or null while it is being moved.</summary>
    public NavHeadingNode? Heading { get; internal set; }

    public string SavedGroupId { get; }
    public int SavedOrder { get; }

    /// <summary>
    /// The heading this screen was under when the screen was last read.
    /// </summary>
    /// <remarks>
    /// A reference rather than an identifier: a heading created on screen has no id until the apply
    /// comes back, so comparing ids would read "moved from unfiled into a new heading" as no change
    /// at all — both sides empty.
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

    public string DisplayTitle => Title.Length > 0 ? Title : DefaultTitle;

    /// <summary>
    /// Displayed, not typed: names are picked from the vocabulary below the field, since free text
    /// can name an icon this build cannot draw.
    /// </summary>
    public string IconName => Icon.Length > 0 ? Icon : DefaultIcon;

    public string Glyph =>
        NavigationIcons.Resolve(Icon, NavigationIcons.Resolve(DefaultIcon, NavigationIcons.Unknown));

    /// <summary>Whether this build can draw the icon name; empty means "use the code's", not
    /// unknown.</summary>
    public bool IsIconKnown => Icon.Length == 0 || NavigationIcons.Knows(Icon);

    public bool IsIconUnknown => !IsIconKnown;

    public string TitleHint => Title.Length == 0
        ? "Using the name the code gives it."
        : $"Changed — the code calls it \"{DefaultTitle}\".";

    public bool IsHidden => !IsVisible;

    public bool CanHide => !HideWhenDenied;

    public string Subtitle => IsOrphan
        ? $"{Id} · not in this build"
        : $"{Id} · {ModuleId}";

    public bool IsModified
    {
        get {
            if (Title != _savedTitle || Icon != _savedIcon || IsVisible != _savedVisible)
            {
                return true;
            }

            if (!ReferenceEquals(Heading, SavedHeading))
            {
                return true;
            }

            return Index != SavedIndex;
        }
    }

    /// <summary>Where it sits under its heading now, counted from zero. -1 while unparented.</summary>
    public int Index => Heading?.Screens.IndexOf(this) ?? -1;

    /// <summary>"2 of 5", for the editor's position control.</summary>
    public string PositionText
    {
        get {
            if (Heading is not { } heading)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.CurrentCulture, "{0} of {1}",
                heading.Screens.IndexOf(this) + 1, heading.Screens.Count);
        }
    }

    /// <summary>How this screen's staged state differs from what is saved, in words.</summary>
    /// <remarks>
    /// One sentence per kind of change rather than one per field — the pending bar and the diff
    /// both read these. Hidden/shown comes first: it is the only change that takes something out
    /// of the pane.
    /// </remarks>
    public IReadOnlyList<string> Changes
    {
        get {
            var changes = new List<string>(4);

            if (IsVisible != _savedVisible)
            {
                changes.Add(IsVisible ? "shown" : "hidden");
            }

            if (!ReferenceEquals(Heading, SavedHeading))
            {
                changes.Add(Heading is { IsUnfiled: false } heading
                    ? $"moved to {heading.Title}"
                    : "unfiled");
            }
            else if (Index != SavedIndex)
            {
                changes.Add("reordered");
            }

            if (Title != _savedTitle)
            {
                changes.Add(Title.Length == 0 ? "name reset" : $"renamed to {Title}");
            }

            if (Icon != _savedIcon)
            {
                changes.Add(Icon.Length == 0 ? "icon reset" : $"icon set to {Icon}");
            }

            return changes;
        }
    }

    public void Discard()
    {
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
    public void PlacementChanged()
    {
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
public sealed partial class NavHeadingNode : ObservableObject
{
    private readonly string _savedTitle;

    public NavHeadingNode(NavGroupRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Id = row.Id;
        IsSystem = row.IsSystem;
        SortOrder = row.SortOrder;
        Title = row.Title;
        _savedTitle = row.Title;
        Screens.CollectionChanged += (_, _) => ScreensChanged();
    }

    private NavHeadingNode(string title, int order, bool unfiled)
    {
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
    public static NavHeadingNode NewHeading(string title, int order)
    {
        return new NavHeadingNode(title, order, unfiled: false);
    }

    /// <summary>
    /// Builds the bucket for screens under no heading at all.
    /// </summary>
    /// <remarks>
    /// Two kinds of row have no heading: a destination nobody has filed, and a leftover from a
    /// module not in this build — which the server lists deliberately, because this screen is the
    /// only place it can be discovered. Not a heading: it has no identifier, is never sent to the
    /// server, and cannot be renamed or deleted.
    /// </remarks>
    public static NavHeadingNode Unfiled()
    {
        return new NavHeadingNode("Not in any heading", int.MaxValue, unfiled: true);
    }

    public string Id { get; internal set; }

    public bool IsSystem { get; }

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
    /// Not part of IsModified: dragging a heading changes its position in the list, not this
    /// number — the number is derived from the position at apply time, and only for headings whose
    /// order moved. Comparing it here would miss a drag, which has not changed it yet.
    /// </remarks>
    public int SortOrder { get; internal set; }

    /// <summary>
    /// Whether the delete button is offered: a product-shipped heading is renamable and
    /// re-orderable, never deletable.
    /// </summary>
    /// <remarks>
    /// The server refuses the delete too; this only keeps the button from being offered.
    /// </remarks>
    public bool CanDelete => !IsSystem && !IsUnfiled;

    /// <summary>The unfiled bucket is not a heading and has no name to rename.</summary>
    public bool CanRename => !IsUnfiled;

    public bool IsBuiltIn => IsSystem;

    public string CountText => Screens.Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Whether to draw the drop target instead of a list.</summary>
    public bool IsEmpty => Screens.Count == 0;

    /// <summary>
    /// Own name and existence only: re-ordering the headings is a fact about the list rather than
    /// about any one of them, so the screen tracks that and this does not.
    /// </summary>
    public bool IsModified => IsNew || Title != _savedTitle;

    /// <summary>How this heading differs from what is saved, in words.</summary>
    public IReadOnlyList<string> Changes
    {
        get {
            if (IsUnfiled)
            {
                return [];
            }

            if (IsNew)
            {
                return ["added"];
            }

            return Title != _savedTitle ? [$"renamed to {Title}"] : [];
        }
    }

    /// <summary>The saved order of the screens under it, for deciding whether it needs renumbering.</summary>
    public IReadOnlyList<string> SavedSequence { get; internal set; } = [];

    public void Discard()
    {
        Title = _savedTitle;
    }

    private void ScreensChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        foreach (var screen in Screens)
        {
            screen.PlacementChanged();
        }
    }
}
