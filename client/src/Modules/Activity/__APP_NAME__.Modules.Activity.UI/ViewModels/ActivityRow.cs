using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using __ROOT_NAMESPACE__.Modules.Activity.Application.Activity;

namespace __ROOT_NAMESPACE__.Modules.Activity.UI.ViewModels;

/// <summary>One key-and-value line inside an expanded row.</summary>
public sealed record ActivityDetail(string Label, string Value);

/// <summary>
/// One row in the feed, which may stand for several entries.
/// </summary>
/// <remarks>
/// An observable object rather than a record because expansion is mutable state the row owns;
/// the facts themselves are get-only. Wording, icon and grouping are decided here from the
/// server's stable kind and structured facts, so the page can be re-worded without a server
/// release.
/// </remarks>
public sealed partial class ActivityRow : ObservableObject
{
    /// <summary>
    /// The glyph drawn for a kind this build does not recognise. Written as an escape, never a
    /// literal: Private Use Area code points render as nothing in an editor and in a diff, so a
    /// literal is destroyed by an edit that looks harmless.
    /// </summary>
    private const string _fallbackGlyph = "\uE946";

    private ActivityRow(IReadOnlyList<ActivityEntryDto> entries)
    {
        Entries = entries;
        var first = entries[0];

        Kind = first.Kind;
        Category = first.Category;

        var (title, glyph, isAlert) = Present(first.Kind);
        Title = title;
        Glyph = glyph;
        IsAlert = isAlert;
        IsNew = first.Kind == ActivityKinds.NewDeviceSignedIn;

        Meta = Join(first.Platform, first.IPAddress);
        TimeText = entries.Count > 1 ? Span(entries) : Moment(first.OccurredAt);
        Occurrences = entries.Count > 1
            ? [.. entries.Select(entry => Occurrence(entry))]
            : [];
        Facts = Describe(first);
    }

    /// <summary>The entries this row stands for, newest first. One, unless it is a burst.</summary>
    public IReadOnlyList<ActivityEntryDto> Entries { get; }

    /// <summary>The kind the server reported, which is what the wording and the badges key on.</summary>
    public string Kind { get; }

    /// <summary>The category the server put this in, for the label the row may carry.</summary>
    public string Category { get; }

    /// <summary>What happened, in this client's words.</summary>
    public string Title { get; }

    /// <summary>The muted line under the title: where it happened.</summary>
    public string Meta { get; }

    public string TimeText { get; }
    public string Glyph { get; }

    /// <summary>Whether the row is drawn as an alert: kinds where "was that me?" could be no.</summary>
    public bool IsAlert { get; }

    /// <summary>Whether to badge this as a first sighting of a device.</summary>
    public bool IsNew { get; }

    /// <summary>The timestamps a burst collapsed, newest first. Empty for a single entry.</summary>
    public IReadOnlyList<string> Occurrences { get; }

    /// <summary>The detail lines shown when the row is opened.</summary>
    public IReadOnlyList<ActivityDetail> Facts { get; }

    /// <summary>
    /// Whether this row offers a way to act on it.
    /// </summary>
    /// <remarks>
    /// Only where the answer to "was that me?" could be no; offering it on every row would make
    /// the offer meaningless.
    /// </remarks>
    public bool CanSecure =>
        Kind is ActivityKinds.FailedSignIn
            or ActivityKinds.NewDeviceSignedIn
            or ActivityKinds.SessionRevokedByAdmin;

    public int Count => Entries.Count;

    public bool IsBurst => Entries.Count > 1;

    /// <summary>The multiplier badge, "×9".</summary>
    public string CountText => "×" + Count.ToString(CultureInfo.CurrentCulture);

    public bool HasMeta => Meta.Length > 0;

    /// <summary>
    /// Whether to label the row with its category.
    /// </summary>
    /// <remarks>
    /// Sign-in rows carry no chip: most of the feed is sessions, so a chip on the exceptions is
    /// information and a chip on every row is decoration.
    /// </remarks>
    public bool ShowCategory => Category.Length > 0 && Category != ActivityCategories.SignIn;

    /// <summary>The category, spelled for a person.</summary>
    public string CategoryText => Category switch {
        ActivityCategories.Security => "Security",
        ActivityCategories.System => "System",
        _ => Category,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnnouncedName))]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// What a screen reader says for this row.
    /// </summary>
    /// <remarks>
    /// The state belongs in the name because the chevron is a glyph inside the row rather than a
    /// control of its own: there is no expander for UIA to report an expand-collapse pattern on, so
    /// a reader pressing Enter had no way to hear that anything had happened.
    /// </remarks>
    public string AnnouncedName
    {
        get {
            string burst = IsBurst ? $", {Count} times" : string.Empty;

            return $"{Title}, {TimeText}{burst}, {(IsExpanded ? "expanded" : "collapsed")}";
        }
    }

    /// <summary>Builds one row per entry, collapsing consecutive repeats into a burst.</summary>
    /// <remarks>
    /// Consecutive is load-bearing: grouping every matching entry in the page would reorder the
    /// feed and misdate the older entries. Only a run of the same fact, from the same session,
    /// inside a short window collapses.
    /// </remarks>
    public static IReadOnlyList<ActivityRow> Collapse(
        IReadOnlyList<ActivityEntryDto> entries, TimeSpan window)
    {
        var rows = new List<ActivityRow>();
        var run = new List<ActivityEntryDto>();

        foreach (var entry in entries)
        {
            if (run.Count > 0 && !Continues(run[^1], entry, window))
            {
                rows.Add(new ActivityRow(run));
                run = [];
            }
            run.Add(entry);
        }

        if (run.Count > 0)
        {
            rows.Add(new ActivityRow(run));
        }

        return rows;
    }

    /// <summary>Whether <paramref name="next"/> is more of the same thing as <paramref name="last"/>.</summary>
    /// <remarks>
    /// Entries with no session are never collapsed even when they match. A password change has no
    /// session, and two of them minutes apart are two decisions somebody made rather than one event
    /// reported twice.
    /// </remarks>
    private static bool Continues(ActivityEntryDto last, ActivityEntryDto next, TimeSpan window)
    {
        return last.Kind == next.Kind
            && last.SessionId.Length > 0
            && last.SessionId == next.SessionId
            && last.OccurredAt - next.OccurredAt <= window;
    }

    private static (string Title, string Glyph, bool IsAlert) Present(string kind)
    {
        // An unrecognised kind shows its raw value rather than being dropped: the server can add
        // kinds without a client release, and a feed that hides them is incomplete.
        return kind switch {
            ActivityKinds.SignedIn => ("Signed in", "\uE930", false),
            ActivityKinds.SignedOut => ("Signed out", "\uE7E8", false),
            ActivityKinds.NewDeviceSignedIn => ("New device signed in", "\uE717", false),
            ActivityKinds.SessionRevoked => ("Session revoked", "\uEE35", false),
            // The one row that says another person acted on this account, so it is drawn as an alert
            // even though it shares the revocation glyph.
            ActivityKinds.SessionRevokedByAdmin =>
                ("Session revoked by an administrator", "\uEE35", true),
            ActivityKinds.FailedSignIn => ("Failed sign-in attempt", "\uE783", true),
            ActivityKinds.PasswordChanged => ("Password changed", "\uE8D7", false),
            ActivityKinds.AppUpdated => ("App updated", "\uE895", false),
            ActivityKinds.ThemeChanged => ("Theme changed", "\uE790", false),
            _ => (kind, _fallbackGlyph, false),
        };
    }

    /// <summary>
    /// The detail lines, with every empty one left out.
    /// </summary>
    /// <remarks>
    /// No "Initiated by" line: no layer records who asked, only what happened, and inventing one
    /// would fabricate data on a security screen. The stored device string is the user agent — the
    /// readable and raw forms both come from that one value.
    /// </remarks>
    private static IReadOnlyList<ActivityDetail> Describe(ActivityEntryDto entry)
    {
        var facts = new List<ActivityDetail>(5) { new("Event", entry.Id) };

        if (entry.SessionId.Length > 0)
        {
            facts.Add(new ActivityDetail("Session", entry.SessionId));
        }

        if (entry.IPAddress.Length > 0)
        {
            facts.Add(new ActivityDetail("IP address", entry.IPAddress));
        }

        if (entry.Platform.Length > 0)
        {
            facts.Add(new ActivityDetail("Platform", entry.Platform));
        }

        if (entry.Device.Length > 0)
        {
            facts.Add(new ActivityDetail("Reported as", entry.Device));
        }

        return facts;
    }

    private static string Occurrence(ActivityEntryDto entry)
    {
        return entry.OccurredAt
            .ToLocalTime()
            .ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    /// <summary>The span a burst covers, oldest to newest.</summary>
    private static string Span(IReadOnlyList<ActivityEntryDto> entries)
    {
        string oldest = entries[^1].OccurredAt
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.CurrentCulture);
        string newest = entries[0].OccurredAt
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.CurrentCulture);

        return oldest == newest ? newest : oldest + "–" + newest;
    }

    /// <summary>Clock time, plus how long ago while that is still the more useful answer.</summary>
    private static string Moment(DateTimeOffset occurred)
    {
        string clock = occurred
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.CurrentCulture);
        string relative = Relative(occurred);

        return relative.Length == 0 ? clock : clock + " · " + relative;
    }

    private static string Relative(DateTimeOffset moment)
    {
        var elapsed = DateTimeOffset.UtcNow - moment;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        // Past a day the day header above the row already says which day it was, so repeating it on
        // every row is noise.
        return string.Empty;
    }

    private static string Join(params string[] parts)
    {
        return string.Join(" · ", parts.Where(part => part.Length > 0));
    }
}

/// <summary>
/// One day's rows, which is what the list groups by.
/// </summary>
/// <remarks>
/// The boundary is the reader's local midnight, not the server's.
/// </remarks>
public sealed class ActivityDay
{
    public ActivityDay(DateTime day, IReadOnlyList<ActivityRow> items)
    {
        Items = items;
        Title = TitleFor(day);
        CountText = items.Count == 1
            ? "1 event"
            : items.Count.ToString(CultureInfo.CurrentCulture) + " events";
    }

    public string Title { get; }
    public string CountText { get; }
    public IReadOnlyList<ActivityRow> Items { get; }

    private static string TitleFor(DateTime day)
    {
        var today = DateTimeOffset.Now.Date;
        var date = day.Date;
        string spelled = date.ToString("dddd · d MMMM", CultureInfo.CurrentCulture);

        if (date == today)
        {
            return "Today · " + date.ToString("d MMMM", CultureInfo.CurrentCulture);
        }

        if (date == today.AddDays(-1))
        {
            return "Yesterday · " + date.ToString("d MMMM", CultureInfo.CurrentCulture);
        }

        return spelled;
    }
}
