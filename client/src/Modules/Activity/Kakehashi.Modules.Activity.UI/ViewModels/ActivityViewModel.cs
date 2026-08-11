using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Common.Controls;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.Modules.Activity.UI.ViewModels {
  /// <summary>One filter chip along the top of the feed.</summary>
  public sealed partial class ActivityChip : ObservableObject {
    public ActivityChip(string label, string category) {
      Label = label;
      Category = category;
    }

    public string Label { get; }
    public string Category { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(AnnouncedName))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnnouncedName))]
    public partial bool IsSelected { get; set; }

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// What a screen reader says for this chip.
    /// </summary>
    /// <remarks>
    /// The count and the selected state are both drawn rather than announced — the count in a second
    /// TextBlock, the selection in a brush — so a reader heard "Security, button" and could not tell
    /// how many there were or which chip the feed was filtered by.
    /// </remarks>
    public string AnnouncedName {
      get {
        return IsSelected ? $"{Label}, {Count}, showing" : $"{Label}, {Count}";
      }
    }
  }

  /// <summary>How far back to look.</summary>
  public sealed record ActivityRange(string Label, int Days) {
    /// <summary>What the picker shows. A ComboBox draws an item by its ToString.</summary>
    public override string ToString() {
      return Label;
    }
  }

  /// <summary>
  /// Presentation logic for the Activity page: the account's own feed, newest first.
  /// </summary>
  /// <remarks>
  /// It reaches the server exclusively through the mediator and has never heard of gRPC.
  /// <para>
  /// The filtering, the counts and the paging are all the server's, and deliberately so. A page that
  /// filtered what it had already fetched would answer "no matches" for something three pages down,
  /// and counts taken over the loaded rows would change as somebody scrolled. What is decided here is
  /// everything that depends on the reader: the wording, the icons, which day a moment belongs to, and
  /// which repeats are one event.
  /// </para>
  /// </remarks>
  public sealed partial class ActivityViewModel : ViewModel {
    /// <summary>
    /// How close two identical facts have to be to count as one.
    /// </summary>
    /// <remarks>
    /// Fifteen minutes is a judgement, not a measurement: long enough to swallow a client retrying a
    /// sign-out, short enough that two deliberate acts an hour apart stay two rows.
    /// </remarks>
    private static readonly TimeSpan _burstWindow = TimeSpan.FromMinutes(15);

    private readonly ISender _sender;
    private readonly IFileSaveService _files;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly IAccountScreen _accountScreen;

    /// <summary>Everything fetched so far, newest first, across every page loaded.</summary>
    private readonly List<ActivityEntryDto> _entries = [];

    private string _nextPageToken = string.Empty;
    private int _retentionDays;
    private int _total;
    private string _appliedSearch = string.Empty;

    /// <summary>
    /// Whether construction has finished.
    /// </summary>
    /// <remarks>
    /// Choosing the default range in the constructor raises the change hook, and without this the view
    /// model would start a network call while it was still being built — before the page that owns it
    /// exists, and in every test that merely constructs one.
    /// </remarks>
    private bool _ready;

    public ActivityViewModel(
        ISender sender,
        IFileSaveService files,
        IClipboardService clipboard,
        INotificationService notifications,
        IAccountScreen accountScreen) {
      ArgumentNullException.ThrowIfNull(sender);
      ArgumentNullException.ThrowIfNull(files);
      ArgumentNullException.ThrowIfNull(clipboard);
      ArgumentNullException.ThrowIfNull(notifications);
      ArgumentNullException.ThrowIfNull(accountScreen);
      _sender = sender;
      _files = files;
      _clipboard = clipboard;
      _notifications = notifications;
      _accountScreen = accountScreen;

      // Seven days: long enough to cover "did anything happen while I was away", short enough that
      // the first page is usually the whole answer.
      SelectedRange = Ranges[1];
      Chips[0].IsSelected = true;
      _ready = true;
    }

    /// <summary>The feed, grouped by the reader's own days, newest first.</summary>
    public ObservableCollection<ActivityDay> Days { get; } = [];

    /// <summary>The four counts along the top.</summary>
    public ObservableCollection<StatCard> StatCards { get; } = [];

    /// <summary>The category filters. "All" first, and it is this client's own idea — see
    /// <see cref="ActivityCategories.All"/>.</summary>
    public ObservableCollection<ActivityChip> Chips { get; } = [
      new("All", ActivityCategories.All),
      new("Sign-ins", ActivityCategories.SignIn),
      new("Security", ActivityCategories.Security),
      new("System", ActivityCategories.System),
    ];

    /// <summary>The ranges offered, none of them longer than the feed is kept.</summary>
    public IReadOnlyList<ActivityRange> Ranges { get; } = [
      new("Last 24 hours", 1),
      new("Last 7 days", 7),
      new("Last 30 days", 30),
      new("Last 90 days", 90),
    ];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ActivityRange SelectedRange { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>"Showing 8 of 214 events · kept for 90 days".</summary>
    [ObservableProperty]
    public partial string CountSummary { get; set; } = string.Empty;

    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Whether to show the empty state: nothing to show, nothing to blame, nothing in flight.
    /// A feed that is loading and a feed that is genuinely empty are different pictures.
    /// </summary>
    public bool IsEmpty => !IsBusy && !HasError && Days.Count == 0;

    /// <summary>Fetches the first page, replacing whatever is on screen.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken) {
      IsBusy = true;
      try {
        var result = await _sender.Send(new GetActivityQuery(Filter()), cancellationToken);
        if (result.IsFailure) {
          Fail(result.Error);
          return;
        }

        ErrorMessage = null;
        _entries.Clear();
        Absorb(result.Value, isFirstPage: true);
      } catch (OperationCanceledException) {
        // The page was navigated away from mid-refresh. Not an error, and nothing to report.
      } finally {
        IsBusy = false;
        OnPropertyChanged(nameof(IsEmpty));
      }
    }

    /// <summary>Appends the next page.</summary>
    /// <remarks>
    /// Appends rather than replaces, and re-groups everything: a page boundary can fall in the middle
    /// of a day or in the middle of a burst, so the last group and the last row have to be rebuilt
    /// with the new entries rather than added after them.
    /// </remarks>
    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken) {
      if (_nextPageToken.Length == 0 || IsLoadingMore) {
        return;
      }

      IsLoadingMore = true;
      try {
        var filter = Filter() with { PageToken = _nextPageToken };
        var result = await _sender.Send(new GetActivityQuery(filter), cancellationToken);
        if (result.IsFailure) {
          Fail(result.Error);
          return;
        }

        ErrorMessage = null;
        Absorb(result.Value, isFirstPage: false);
      } catch (OperationCanceledException) {
        // Navigated away mid-page. The rows already on screen are still true.
      } finally {
        IsLoadingMore = false;
      }
    }

    /// <summary>Applies the search box. Submitted rather than typed — see the remark.</summary>
    /// <remarks>
    /// The search runs on the server, so a request per keystroke would be a request per keystroke.
    /// Clearing the box is different: it applies immediately, because nobody expects to press Enter
    /// to stop filtering.
    /// </remarks>
    [RelayCommand]
    private Task SearchAsync(CancellationToken cancellationToken) {
      _appliedSearch = SearchText.Trim();
      return LoadAsync(cancellationToken);
    }

    /// <summary>Selects one category chip.</summary>
    [RelayCommand]
    private Task SelectCategoryAsync(ActivityChip chip) {
      ArgumentNullException.ThrowIfNull(chip);
      if (chip.IsSelected) {
        return Task.CompletedTask;
      }

      foreach (var other in Chips) {
        other.IsSelected = ReferenceEquals(other, chip);
      }
      return LoadAsync(CancellationToken.None);
    }

    /// <summary>Puts one entry on the clipboard, for pasting into a support conversation.</summary>
    [RelayCommand]
    private void CopyEvent(ActivityRow row) {
      ArgumentNullException.ThrowIfNull(row);

      var text = new StringBuilder();
      text.Append(row.Title).Append(" · ")
          .Append(row.Entries[0].OccurredAt.ToString("u", CultureInfo.InvariantCulture));
      foreach (var fact in row.Facts) {
        text.Append('\n').Append(fact.Label).Append(": ").Append(fact.Value);
      }

      _clipboard.SetText(text.ToString());
      _notifications.Show("Event copied.", InfoBarSeverity.Success);
    }

    /// <summary>Takes somebody to the screen where they can end sessions and change a password.</summary>
    /// <remarks>
    /// Through a port, because the screen belongs to another module and the only join available is a
    /// navigation key with nothing checking it — see <see cref="IAccountScreen"/>. The failure is
    /// handled rather than assumed: if that module is not mounted, saying where to go is better than a
    /// link that does nothing.
    /// </remarks>
    [RelayCommand]
    private void SecureAccount() {
      if (!_accountScreen.Open()) {
        _notifications.Show(
            "Open the Account page to review your sessions and change your password.",
            InfoBarSeverity.Informational);
      }
    }

    /// <summary>Writes what is on screen to a CSV file.</summary>
    /// <remarks>
    /// What is on screen, and it says so: the file holds the entries that have been loaded, not every
    /// entry in the range. Exporting the whole range would mean paging the entire feed behind a button
    /// that looks instant.
    /// </remarks>
    [RelayCommand]
    private async Task ExportAsync() {
      if (_entries.Count == 0) {
        _notifications.Show("There is nothing to export.", InfoBarSeverity.Informational);
        return;
      }

      string? path = await _files.PickSaveLocationAsync(
          $"activity-{DateTime.Now:yyyyMMdd-HHmmss}.csv", "CSV file", ".csv");
      if (path is null) {
        return;
      }

      var csv = new StringBuilder("When,Kind,Category,Platform,Address,Session,Event\n");
      foreach (var entry in _entries) {
        csv.Append(Csv(entry.OccurredAt.ToString("u", CultureInfo.InvariantCulture))).Append(',')
            .Append(Csv(entry.Kind)).Append(',')
            .Append(Csv(entry.Category)).Append(',')
            .Append(Csv(entry.Platform)).Append(',')
            .Append(Csv(entry.IPAddress)).Append(',')
            .Append(Csv(entry.SessionId)).Append(',')
            .Append(Csv(entry.Id)).Append('\n');
      }

      try {
        await File.WriteAllTextAsync(path, csv.ToString());
      } catch (IOException exception) {
        _notifications.Show($"Could not write the file: {exception.Message}", InfoBarSeverity.Error);
        return;
      }

      _notifications.Show($"Exported {_entries.Count} events.", InfoBarSeverity.Success);
    }

    partial void OnSelectedRangeChanged(ActivityRange value) {
      if (!_ready) {
        return;
      }
      _ = LoadCommand.ExecuteAsync(parameter: null);
    }

    partial void OnSearchTextChanged(string value) {
      // Only the emptying case. Applying on every keystroke would be a server round-trip per
      // character; leaving a cleared box filtered would be a list that stays narrow for no visible
      // reason.
      if (value.Length == 0 && _appliedSearch.Length > 0) {
        _appliedSearch = string.Empty;
        _ = LoadCommand.ExecuteAsync(parameter: null);
      }
    }

    /// <summary>What to ask for, from the state of the filter bar.</summary>
    private ActivityFeedFilter Filter() {
      string category = Chips.FirstOrDefault(chip => chip.IsSelected)?.Category
          ?? ActivityCategories.All;

      return new ActivityFeedFilter {
        From = DateTimeOffset.Now.AddDays(-SelectedRange.Days),
        Category = category,
        Search = _appliedSearch,
      };
    }

    private void Fail(Error error) {
      ErrorMessage = error.Message;

      // Cleared for two failures and no others. A page left open across a sign-out keeps refreshing,
      // and a feed that held its rows would go on showing the previous account's devices and addresses
      // to whoever signs in next. A lost page means this client and the server disagree about where
      // the reader is, so what is on screen cannot be trusted to continue from. A network blip is
      // different: there the rows are still true, and throwing them away would make a flaky connection
      // look like an empty history.
      if (error == ActivityErrors.NotSignedIn || error == ActivityErrors.PageLost) {
        _entries.Clear();
        _nextPageToken = string.Empty;
        HasMore = false;
        Regroup();
      }
    }

    /// <summary>Takes in a page: its entries, its counts, and what it says about the whole feed.</summary>
    private void Absorb(ActivityPageDto page, bool isFirstPage) {
      _entries.AddRange(page.Entries);
      _nextPageToken = page.NextPageToken;
      HasMore = page.HasMore;
      _total = page.Total;

      // Only the first page carries the counts, so a later page must not overwrite them. Asking the
      // reply whether it has any would look equivalent and is not: an account whose feed is genuinely
      // empty sends no counts either, and every chip would then keep whatever it last showed.
      if (isFirstPage) {
        _retentionDays = page.RetentionDays;
        foreach (var chip in Chips) {
          chip.Count = chip.Category == ActivityCategories.All
              ? page.Total
              : page.CountIn(chip.Category);
        }
        RebuildStatCards(page);
      }

      Regroup();
      RebuildSummary();
    }

    /// <summary>Rebuilds the rows and the day groups from everything loaded.</summary>
    /// <remarks>
    /// Which rows were open is carried across the rebuild. Every row is a new object here — a burst
    /// can grow a member and stop being the row it was — so without this, pressing "load more" while
    /// reading an expanded entry closed it, which reads as the app losing the reader's place.
    /// </remarks>
    private void Regroup() {
      var open = new HashSet<string>(StringComparer.Ordinal);
      foreach (var day in Days) {
        foreach (var row in day.Items) {
          if (row.IsExpanded) {
            open.Add(row.Entries[0].Id);
          }
        }
      }

      var rows = ActivityRow.Collapse(_entries, _burstWindow);

      Days.Clear();
      foreach (var group in rows.GroupBy(row => row.Entries[0].OccurredAt.ToLocalTime().Date)) {
        var items = group.ToList();
        foreach (var row in items) {
          row.IsExpanded = open.Contains(row.Entries[0].Id);
        }
        Days.Add(new ActivityDay(group.Key, [.. items]));
      }
      OnPropertyChanged(nameof(IsEmpty));
    }

    private void RebuildSummary() {
      int shown = _entries.Count;
      string kept = _retentionDays > 0
          ? $" · kept for {_retentionDays} days"
          : string.Empty;

      CountSummary = shown == _total
          ? $"{shown} {Events(shown)}{kept}"
          : $"Showing {shown} of {_total} {Events(_total)}{kept}";
    }

    /// <summary>
    /// The four cards, each stating something the server counted rather than something inferred from
    /// the page that happens to be loaded.
    /// </summary>
    /// <remarks>
    /// The mockup this page follows had a "Devices" card counting distinct machines over the range.
    /// That count does not exist: the server groups by kind and by category, not by device, and
    /// counting the loaded rows would give a number that grew as somebody pressed "load more". The
    /// card here says what it actually counts.
    /// </remarks>
    private void RebuildStatCards(ActivityPageDto page) {
      int refused = page.CountOf(ActivityKinds.FailedSignIn);
      int signIns = page.CountOf(ActivityKinds.SignedIn)
          + page.CountOf(ActivityKinds.NewDeviceSignedIn);
      var platforms = _entries
          .Select(entry => entry.Platform)
          .Where(platform => platform.Length > 0)
          .Distinct(StringComparer.Ordinal)
          .ToList();

      StatCards.Clear();
      StatCards.Add(new StatCard(
          "EVENTS", page.Total.ToString(CultureInfo.CurrentCulture),
          SelectedRange.Label.ToLowerInvariant(), "", StatKind.Accent));
      StatCards.Add(new StatCard(
          "SIGN-INS", signIns.ToString(CultureInfo.CurrentCulture),
          $"{page.CountOf(ActivityKinds.NewDeviceSignedIn)} from a new device", "",
          StatKind.Positive));
      StatCards.Add(new StatCard(
          "REFUSED SIGN-INS", refused.ToString(CultureInfo.CurrentCulture),
          refused == 0 ? "none in this range" : "review the rows below", "",
          refused == 0 ? StatKind.Muted : StatKind.Critical));
      StatCards.Add(new StatCard(
          "PLATFORMS IN VIEW", platforms.Count.ToString(CultureInfo.CurrentCulture),
          platforms.Count == 0 ? "none reported" : string.Join(" · ", platforms), "",
          StatKind.Muted));
    }

    private static string Events(int count) {
      return count == 1 ? "event" : "events";
    }

    /// <summary>Quotes a CSV field, doubling any quote inside it.</summary>
    private static string Csv(string value) {
      return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
  }
}
