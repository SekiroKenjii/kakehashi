using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.Modules.Activity.Application.Activity;

namespace Kakehashi.Modules.Activity.UI.ViewModels {
  public sealed record ActivityDetail(string Label, string Value);

  // An observable object rather than a record because a row is expandable, and expansion is state
  // the row owns. The facts themselves are get-only: an entry never changes once it happened.
  public sealed partial class ActivityRow : ObservableObject {
    // Glyphs are written as escapes, not literal characters: these are Private Use Area code
    // points, so a literal shows as nothing at all in an editor and in a diff — which is how a
    // glyph mapping gets destroyed by an edit that looked harmless. The older screens in this repo
    // still hold literals; this is the direction to move them.
    private const string _fallbackGlyph = "\uE946";

    private ActivityRow(IReadOnlyList<ActivityEntryDto> entries) {
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

    // Newest first. One entry unless this row is a burst.
    public IReadOnlyList<ActivityEntryDto> Entries { get; }

    public string Kind { get; }

    public string Category { get; }

    public string Title { get; }

    public string Meta { get; }

    public string TimeText { get; }
    public string Glyph { get; }

    public bool IsAlert { get; }

    public bool IsNew { get; }

    // Newest first. Empty for a single entry.
    public IReadOnlyList<string> Occurrences { get; }

    public IReadOnlyList<ActivityDetail> Facts { get; }

    // Only where the answer to "was that me?" could be no. Offering it on every row would make the
    // offer meaningless, which is the same reason the mockup only draws it twice.
    public bool CanSecure =>
        Kind is ActivityKinds.FailedSignIn
            or ActivityKinds.NewDeviceSignedIn
            or ActivityKinds.SessionRevokedByAdmin;

    public int Count => Entries.Count;

    public bool IsBurst => Entries.Count > 1;

    public string CountText => "×" + Count.ToString(CultureInfo.CurrentCulture);

    public bool HasMeta => Meta.Length > 0;

    // Sign-in rows are not labelled. A feed of an account's activity is mostly sessions, so a chip
    // repeating "SignIn" on two rows in three is decoration; a chip on the ones that are not is
    // information.
    public bool ShowCategory => Category.Length > 0 && Category != ActivityCategories.SignIn;

    public string CategoryText => Category switch {
      ActivityCategories.Security => "Security",
      ActivityCategories.System => "System",
      _ => Category,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnnouncedName))]
    public partial bool IsExpanded { get; set; }

    // The expanded state belongs in the name because the chevron is a glyph inside the row rather
    // than a control of its own: there is no expander for UIA to report an expand-collapse pattern
    // on, so a reader pressing Enter had no way to hear that anything had happened.
    public string AnnouncedName {
      get {
        string burst = IsBurst ? $", {Count} times" : string.Empty;
        return $"{Title}, {TimeText}{burst}, {(IsExpanded ? "expanded" : "collapsed")}";
      }
    }

    // Consecutive is load-bearing. Grouping every matching entry in the page would reorder the feed
    // — nine sign-outs from this morning would swallow one from last week and claim it happened at
    // breakfast. Only a run of the same fact, from the same session, inside a short window is one
    // event as far as a reader is concerned.
    public static IReadOnlyList<ActivityRow> Collapse(
        IReadOnlyList<ActivityEntryDto> entries, TimeSpan window) {
      var rows = new List<ActivityRow>();
      var run = new List<ActivityEntryDto>();

      foreach (var entry in entries) {
        if (run.Count > 0 && !Continues(run[^1], entry, window)) {
          rows.Add(new ActivityRow(run));
          run = [];
        }
        run.Add(entry);
      }
      if (run.Count > 0) {
        rows.Add(new ActivityRow(run));
      }
      return rows;
    }

    // Entries with no session are never collapsed even when they match. A password change has no
    // session, and two of them minutes apart are two decisions somebody made rather than one event
    // reported twice.
    private static bool Continues(ActivityEntryDto last, ActivityEntryDto next, TimeSpan window) {
      return last.Kind == next.Kind
          && last.SessionId.Length > 0
          && last.SessionId == next.SessionId
          && last.OccurredAt - next.OccurredAt <= window;
    }

    private static (string Title, string Glyph, bool IsAlert) Present(string kind) {
      // An unrecognised kind shows its raw value rather than being dropped. Every module added here
      // contributes kinds of its own, and a feed that silently hides what it does not recognise is
      // a feed you cannot trust to be complete.
      return kind switch {
        ActivityKinds.SignedIn => ("Signed in", "\uE930", false),
        ActivityKinds.SignedOut => ("Signed out", "\uE7E8", false),
        ActivityKinds.NewDeviceSignedIn => ("New device signed in", "\uE717", false),
        ActivityKinds.SessionRevoked => ("Session revoked", "\uEE35", false),
        // The one row saying another person acted on this account, so it is an alert even though it
        // shares the revocation glyph.
        ActivityKinds.SessionRevokedByAdmin =>
            ("Session revoked by an administrator", "\uEE35", true),
        ActivityKinds.FailedSignIn => ("Failed sign-in attempt", "\uE783", true),
        ActivityKinds.PasswordChanged => ("Password changed", "\uE8D7", false),
        ActivityKinds.AppUpdated => ("App updated", "\uE895", false),
        ActivityKinds.ThemeChanged => ("Theme changed", "\uE790", false),
        _ => (kind, _fallbackGlyph, false),
      };
    }

    // The mockup this page follows also drew an "Initiated by" line. There is no such field, at any
    // layer — the server records what happened, not who asked — and inventing one would be a
    // fabrication on the single screen somebody opens to check whether a stranger has been in their
    // account. It is absent rather than guessed.
    //
    // "Device" and "User agent" were two lines in the mockup and are one fact here: the stored
    // device string *is* the user agent. Both forms are shown — the readable one and the raw one —
    // but they come from one value rather than from two that could disagree.
    private static IReadOnlyList<ActivityDetail> Describe(ActivityEntryDto entry) {
      var facts = new List<ActivityDetail>(5) { new("Event", entry.Id) };
      if (entry.SessionId.Length > 0) {
        facts.Add(new ActivityDetail("Session", entry.SessionId));
      }
      if (entry.IPAddress.Length > 0) {
        facts.Add(new ActivityDetail("IP address", entry.IPAddress));
      }
      if (entry.Platform.Length > 0) {
        facts.Add(new ActivityDetail("Platform", entry.Platform));
      }
      if (entry.Device.Length > 0) {
        facts.Add(new ActivityDetail("Reported as", entry.Device));
      }
      return facts;
    }

    private static string Occurrence(ActivityEntryDto entry) {
      return entry.OccurredAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private static string Span(IReadOnlyList<ActivityEntryDto> entries) {
      string oldest = entries[^1].OccurredAt.ToLocalTime()
          .ToString("HH:mm", CultureInfo.CurrentCulture);
      string newest = entries[0].OccurredAt.ToLocalTime()
          .ToString("HH:mm", CultureInfo.CurrentCulture);
      return oldest == newest ? newest : oldest + "–" + newest;
    }

    private static string Moment(DateTimeOffset occurred) {
      string clock = occurred.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
      string relative = Relative(occurred);
      return relative.Length == 0 ? clock : clock + " · " + relative;
    }

    private static string Relative(DateTimeOffset moment) {
      var elapsed = DateTimeOffset.UtcNow - moment;
      if (elapsed < TimeSpan.FromMinutes(1)) {
        return "just now";
      }
      if (elapsed < TimeSpan.FromHours(1)) {
        return $"{(int)elapsed.TotalMinutes}m ago";
      }
      if (elapsed < TimeSpan.FromDays(1)) {
        return $"{(int)elapsed.TotalHours}h ago";
      }

      // Past a day the day header above the row already says which day it was, so repeating it on
      // every row is noise.
      return string.Empty;
    }

    private static string Join(params string[] parts) {
      return string.Join(" · ", parts.Where(part => part.Length > 0));
    }
  }

  // The day boundary is the reader's local midnight, not the server's. A sign-in at 00:30 in Ho Chi
  // Minh City belongs under today for the person reading it, whatever UTC thinks.
  public sealed class ActivityDay {
    public ActivityDay(DateTime day, IReadOnlyList<ActivityRow> items) {
      Items = items;
      Title = TitleFor(day);
      CountText = items.Count == 1
          ? "1 event"
          : items.Count.ToString(CultureInfo.CurrentCulture) + " events";
    }

    public string Title { get; }
    public string CountText { get; }
    public IReadOnlyList<ActivityRow> Items { get; }

    private static string TitleFor(DateTime day) {
      var today = DateTimeOffset.Now.Date;
      var date = day.Date;
      string spelled = date.ToString("dddd · d MMMM", CultureInfo.CurrentCulture);

      if (date == today) {
        return "Today · " + date.ToString("d MMMM", CultureInfo.CurrentCulture);
      }
      if (date == today.AddDays(-1)) {
        return "Yesterday · " + date.ToString("d MMMM", CultureInfo.CurrentCulture);
      }
      return spelled;
    }
  }
}
