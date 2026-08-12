using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.UI;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kakehashi.App.Services {
  public sealed record AppActivityEntry(
      string Kind, string Title, string Detail, DateTimeOffset OccurredAt);

  // Entries are kept newest-first in a capped feed persisted via ILocalSettingsService. Awake at
  // startup so events that happen before any page exists — the startup sign-in, an app update —
  // are still recorded. Recording is marshalled to the UI thread, which keeps the settings store
  // single-threaded.
  public sealed class AppActivityLog : IAwakeOnStartup {
    public const string SignedInKind = "SignedIn";
    public const string SignedOutKind = "SignedOut";
    public const string AppUpdatedKind = "AppUpdated";
    public const string ThemeChangedKind = "ThemeChanged";
    private const string _entriesKey = "App.ActivityLog";
    private const string _lastVersionKey = "App.LastRunVersion";
    // Mirrors the private key ThemeService persists under, so the first "Theme changed" entry can
    // show the correct old theme (the theme service itself has not initialized yet when we awake).
    private const string _themeSettingKey = "AppTheme";
    private const int _maxEntries = 50;

    private readonly ILocalSettingsService _localSettings;
    private readonly List<AppActivityEntry> _entries;
    private IAuthSessionAccessor? _sessionAccessor;
    private DispatcherQueue? _dispatcherQueue;
    private bool _wasAuthenticated;
    private ElementTheme _lastTheme;

    public AppActivityLog(ILocalSettingsService localSettings) {
      ArgumentNullException.ThrowIfNull(localSettings);
      _localSettings = localSettings;
      _entries = _localSettings.Read<List<AppActivityEntry>>(_entriesKey) ?? [];
    }

    public string Name => "App activity log";

    public void Initialize(IServiceProvider serviceProvider) {
      ArgumentNullException.ThrowIfNull(serviceProvider);
      _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

      // Deferred by one turn of the message loop, deliberately: startup creates every awake service
      // in registration order, so anything registering for the announcement after this one would
      // miss an app update — the single run on which the update is worth announcing at all.
      // There is no dispatcher on a thread that is not a UI thread, which is every thread a unit
      // test runs on; the rest of this class guards it the same way for the same reason.
      if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(RecordAppUpdateIfAny)) {
        RecordAppUpdateIfAny();
      }

      _lastTheme = _localSettings.Read<ElementTheme>(_themeSettingKey);
      // App-lifetime singleton: the subscription is deliberately never disposed.
      _ = serviceProvider.GetRequiredService<IThemeService>().OnThemeChanged
          .Subscribe(OnThemeChanged);

      _sessionAccessor = serviceProvider.GetRequiredService<IAuthSessionAccessor>();
      _wasAuthenticated = _sessionAccessor.Current is not null;
      WeakReferenceMessenger.Default.Register<AppActivityLog, AuthSessionChangedMessage>(
          this, static (log, message) => log.HandleAuthSessionChanged());
    }

    public IReadOnlyList<AppActivityEntry> GetRecent() {
      return [.. _entries];
    }

    public void Record(string kind, string title, string detail) {
      _entries.Insert(0, new AppActivityEntry(kind, title, detail, DateTimeOffset.UtcNow));
      if (_entries.Count > _maxEntries) {
        _entries.RemoveRange(_maxEntries, _entries.Count - _maxEntries);
      }

      _localSettings.Save(_entriesKey, _entries);

      // Announced as well as stored: two of these are facts no server can observe for itself, and
      // the activity module forwards them. The string kinds stay here because they are the shape of
      // what is already persisted in settings; the announcement carries the shared enum instead, so
      // the two assemblies cannot drift apart on a literal.
      if (Announceable(kind) is { } announced) {
        WeakReferenceMessenger.Default.Send(new AppActivityRecordedMessage(announced));
      }
    }

    private static AppActivityKind? Announceable(string kind) {
      return kind switch {
        SignedInKind => AppActivityKind.SignedIn,
        SignedOutKind => AppActivityKind.SignedOut,
        AppUpdatedKind => AppActivityKind.AppUpdated,
        ThemeChangedKind => AppActivityKind.ThemeChanged,
        _ => null,
      };
    }

    private void HandleAuthSessionChanged() {
      // The auth session can change on a background thread; recording happens on the UI thread.
      if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(OnAuthSessionChanged)) {
        OnAuthSessionChanged();
      }
    }

    private void OnAuthSessionChanged() {
      bool isAuthenticated = _sessionAccessor?.Current is not null;
      if (isAuthenticated == _wasAuthenticated) {
        return;
      }

      _wasAuthenticated = isAuthenticated;
      string detail = $"Windows · {Environment.MachineName}";
      if (isAuthenticated) {
        Record(SignedInKind, "Signed in · this device", detail);
      } else {
        Record(SignedOutKind, "Signed out · this device", detail);
      }
    }

    private void OnThemeChanged(ElementTheme theme) {
      if (theme == _lastTheme) {
        return;
      }

      Record(ThemeChangedKind, "Theme changed", $"{ThemeText(_lastTheme)} → {ThemeText(theme)}");
      _lastTheme = theme;
    }

    private void RecordAppUpdateIfAny() {
      string current = VersionText();
      string? previous = _localSettings.Read<string>(_lastVersionKey);
      if (previous is not null && previous != current) {
        Record(AppUpdatedKind, "App updated", $"{previous} → {current}");
      }

      if (previous != current) {
        _localSettings.Save(_lastVersionKey, current);
      }
    }

    private static string ThemeText(ElementTheme theme) {
      return theme switch {
        ElementTheme.Light => "Light",
        ElementTheme.Dark => "Dark",
        _ => "System",
      };
    }

    private static string VersionText() {
      var version = typeof(AppActivityLog).Assembly.GetName().Version;
      return version is null ? "v1.0.0" : $"v{version.ToString(3)}";
    }
  }
}
