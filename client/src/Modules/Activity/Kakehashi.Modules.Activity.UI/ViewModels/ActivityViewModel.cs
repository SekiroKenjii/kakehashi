using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity;
using Kakehashi.UI.Contracts;

namespace Kakehashi.Modules.Activity.UI.ViewModels {
  /// <summary>A row in the activity feed.</summary>
  public sealed record ActivityListItem(
      string Title, string Details, string TimeText, string Glyph) {
    public bool HasDetails => Details.Length > 0;
  }

  /// <summary>
  /// Presentation logic for the Activity page: the account's own feed, newest first.
  /// </summary>
  /// <remarks>
  /// It reaches the server exclusively through the mediator and has never heard of gRPC.
  /// <para>
  /// The wording of each row is chosen here rather than sent by the server. The server ships a
  /// stable kind and the structured facts, which is what lets this page localize, re-word and
  /// re-illustrate the feed without a server release — and what stops the server from owning
  /// presentation for a client it cannot see.
  /// </para>
  /// </remarks>
  public sealed partial class ActivityViewModel : ViewModel {
    private const int _take = 50;

    private readonly ISender _sender;

    public ActivityViewModel(ISender sender) {
      ArgumentNullException.ThrowIfNull(sender);
      _sender = sender;
    }

    /// <summary>The feed, newest first.</summary>
    public ObservableCollection<ActivityListItem> Feed { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Whether the last load failed.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Whether to show the empty state: nothing to show, nothing to blame, nothing in flight.
    /// A feed that is loading and a feed that is genuinely empty are different pictures.
    /// </summary>
    public bool IsEmpty => !IsBusy && !HasError && Feed.Count == 0;

    /// <summary>Fetches the feed.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken) {
      IsBusy = true;
      try {
        var result = await _sender.Send(new GetActivityQuery(_take), cancellationToken);
        if (result.IsFailure) {
          ErrorMessage = result.Error.Message;

          // Cleared for this one failure and no other. A page left open across a sign-out keeps
          // refreshing, and a feed that held its rows would go on showing the previous account's
          // devices and IP addresses to whoever signs in next. A network blip is different: there
          // the rows are still true, and throwing them away would make a flaky connection look
          // like an empty history.
          if (result.Error == ActivityErrors.NotSignedIn) {
            Feed.Clear();
          }
          return;
        }

        ErrorMessage = null;
        Feed.Clear();
        foreach (var entry in result.Value) {
          Feed.Add(ToListItem(entry));
        }
      } catch (OperationCanceledException) {
        // The page was navigated away from mid-refresh. Not an error, and nothing to report.
      } finally {
        IsBusy = false;
        OnPropertyChanged(nameof(IsEmpty));
      }
    }

    private static ActivityListItem ToListItem(ActivityEntryDto entry) {
      // An unrecognised kind shows its raw value rather than being dropped. Every module added to
      // this boilerplate contributes kinds of its own, and a feed that silently hides what it does
      // not recognise is a feed you cannot trust to be complete.
      var (title, glyph) = entry.Kind switch {
        "SignedIn" => ("Signed in", ""),
        "SignedOut" => ("Signed out", ""),
        "PasswordChanged" => ("Password changed", ""),
        _ => (entry.Kind, ""),
      };

      return new ActivityListItem(
          title, JoinDetails(entry.Device, entry.IPAddress), FormatRelative(entry.OccurredAt),
          glyph);
    }

    private static string JoinDetails(params string?[] parts) {
      return string.Join(" · ", Array.FindAll(parts, part => !string.IsNullOrEmpty(part)));
    }

    private static string FormatRelative(DateTimeOffset moment) {
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
      if (elapsed < TimeSpan.FromDays(7)) {
        return $"{(int)elapsed.TotalDays}d ago";
      }
      return moment.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
  }
}
