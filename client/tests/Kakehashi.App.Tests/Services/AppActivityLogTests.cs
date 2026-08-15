using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.App.Services;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.Modules.Auth.UI;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AppActivityLog"/>: the directly-recordable feed (insert order, cap,
/// persistence, read-back) and the <see cref="AppActivityLog.Initialize"/>-driven events
/// (theme change, app update, sign-in transition). The log registers on the static
/// <c>WeakReferenceMessenger</c> during initialization, so each initialized instance is
/// unregistered on teardown to keep the shared bus clean.
/// </summary>
public sealed class AppActivityLogTests : IDisposable
{
    private const string _entriesKey = "App.ActivityLog";
    private const string _lastVersionKey = "App.LastRunVersion";

    private readonly List<AppActivityLog> _initialized = [];

    public void Dispose()
    {
        foreach (var log in _initialized)
        {
            WeakReferenceMessenger.Default.UnregisterAll(log);
        }
    }

    /// <summary>
    /// Recording announces, because two of these facts are ones no server can observe for itself and
    /// a feature module forwards them. The log keeps its own string kinds — that is the shape already
    /// persisted in settings — and announces the shared enum, so neither side depends on a literal
    /// the other might edit.
    /// </summary>
    [Theory]
    [InlineData(AppActivityLog.SignedInKind, AppActivityKind.SignedIn)]
    [InlineData(AppActivityLog.SignedOutKind, AppActivityKind.SignedOut)]
    [InlineData(AppActivityLog.AppUpdatedKind, AppActivityKind.AppUpdated)]
    [InlineData(AppActivityLog.ThemeChangedKind, AppActivityKind.ThemeChanged)]
    public void Record_AnnouncesTheMatchingKind(string kind, AppActivityKind expected)
    {
        var log = new AppActivityLog(new InMemoryLocalSettings());
        var announced = new List<AppActivityKind>();
        object recipient = new();
        WeakReferenceMessenger.Default.Register<AppActivityRecordedMessage>(
            recipient, (_, message) => announced.Add(message.Kind));

        try
        {
            log.Record(kind, "Title", "detail");
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.Equal([expected], announced);
    }

    /// <summary>A kind nobody shares stays local rather than being announced as something else.</summary>
    [Fact]
    public void Record_DoesNotAnnounceAKindNothingElseKnows()
    {
        var log = new AppActivityLog(new InMemoryLocalSettings());
        var announced = new List<AppActivityKind>();
        object recipient = new();
        WeakReferenceMessenger.Default.Register<AppActivityRecordedMessage>(
            recipient, (_, message) => announced.Add(message.Kind));

        try
        {
            log.Record("SomethingLocal", "Title", "detail");
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.Empty(announced);
    }

    [Fact]
    public void Record_InsertsNewestFirst()
    {
        var log = new AppActivityLog(new InMemoryLocalSettings());

        log.Record("K1", "First", "d1");
        log.Record("K2", "Second", "d2");

        var recent = log.GetRecent();
        Assert.Equal("Second", recent[0].Title);
        Assert.Equal("First", recent[1].Title);
    }

    [Fact]
    public void Record_CapsAtFiftyEntries()
    {
        var log = new AppActivityLog(new InMemoryLocalSettings());

        for (int i = 0; i < 55; i++)
        {
            log.Record("K", $"e{i}", "d");
        }

        var recent = log.GetRecent();
        Assert.Equal(50, recent.Count);
        Assert.Equal("e54", recent[0].Title);   // newest kept
        Assert.Equal("e5", recent[49].Title);   // e0..e4 dropped
    }

    [Fact]
    public void Record_PersistsToSettings()
    {
        var settings = new InMemoryLocalSettings();
        var log = new AppActivityLog(settings);

        log.Record("K", "Persisted", "d");

        var stored = settings.Read<List<AppActivityEntry>>(_entriesKey);
        Assert.NotNull(stored);
        Assert.Equal("Persisted", Assert.Single(stored).Title);
    }

    [Fact]
    public void Constructor_ReadsPersistedEntries()
    {
        var settings = new InMemoryLocalSettings();
        settings.Save(_entriesKey, new List<AppActivityEntry> {
    new("K", "Seeded", "d", DateTimeOffset.UtcNow),
  });

        var log = new AppActivityLog(settings);

        Assert.Equal("Seeded", log.GetRecent()[0].Title);
    }

    [Fact]
    public void ThemeChange_RecordsEntryWithOldAndNewTheme()
    {
        var themeChanges = new Subject<ElementTheme>();
        var log = InitLog(new InMemoryLocalSettings(), SignedOutAccessor(), themeChanges);

        themeChanges.OnNext(ElementTheme.Dark);

        var recent = log.GetRecent();
        Assert.Equal(AppActivityLog.ThemeChangedKind, recent[0].Kind);
        Assert.Equal("System → Dark", recent[0].Detail);
    }

    [Fact]
    public void ThemeChange_ToSameTheme_RecordsOnce()
    {
        var themeChanges = new Subject<ElementTheme>();
        var log = InitLog(new InMemoryLocalSettings(), SignedOutAccessor(), themeChanges);

        themeChanges.OnNext(ElementTheme.Dark);
        themeChanges.OnNext(ElementTheme.Dark);

        Assert.Single(log.GetRecent());
    }

    [Fact]
    public void Initialize_RecordsAppUpdate_WhenStoredVersionDiffers()
    {
        var settings = new InMemoryLocalSettings();
        settings.Save(_lastVersionKey, "v0.0.1-old");

        var log = InitLog(settings, SignedOutAccessor(), new Subject<ElementTheme>());

        var recent = log.GetRecent();
        Assert.Equal(AppActivityLog.AppUpdatedKind, recent[0].Kind);
        Assert.StartsWith("v0.0.1-old → ", recent[0].Detail);
    }

    [Fact]
    public void Initialize_DoesNotRecordAppUpdate_WhenVersionUnchanged()
    {
        var settings = new InMemoryLocalSettings();
        settings.Save(_lastVersionKey, CurrentVersionText());

        var log = InitLog(settings, SignedOutAccessor(), new Subject<ElementTheme>());

        Assert.Empty(log.GetRecent());
    }

    [Fact]
    public void AuthSessionChange_ToSignedIn_RecordsSignIn()
    {
        AuthSession? current = null;
        var accessor = Substitute.For<IAuthSessionAccessor>();
        accessor.Current.Returns(_ => current);
        var log = InitLog(new InMemoryLocalSettings(), accessor, new Subject<ElementTheme>());

        current = AuthSession.Create(
            "token", null, null, DateTimeOffset.UtcNow.AddHours(1), "Vo").Value;
        WeakReferenceMessenger.Default.Send(new AuthSessionChangedMessage());

        Assert.Equal(AppActivityLog.SignedInKind, log.GetRecent()[0].Kind);
    }

    private AppActivityLog InitLog(
        ILocalSettingsService settings,
        IAuthSessionAccessor accessor,
        IObservable<ElementTheme> themeChanges)
    {
        var theme = Substitute.For<IThemeService>();
        theme.OnThemeChanged.Returns(themeChanges);
        var log = new AppActivityLog(settings);
        log.Initialize(new StubProvider(theme, accessor));
        _initialized.Add(log);

        return log;
    }

    private static IAuthSessionAccessor SignedOutAccessor()
    {
        // Current returns null by default — no signed-in session.
        return Substitute.For<IAuthSessionAccessor>();
    }

    private static string CurrentVersionText()
    {
        var version = typeof(AppActivityLog).Assembly.GetName().Version;

        return version is null ? "v1.0.0" : $"v{version.ToString(3)}";
    }

    /// <summary>Resolves only the two services <see cref="AppActivityLog.Initialize"/> requires.</summary>
    private sealed class StubProvider(IThemeService theme, IAuthSessionAccessor accessor)
        : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IThemeService))
            {
                return theme;
            }

            if (serviceType == typeof(IAuthSessionAccessor))
            {
                return accessor;
            }

            return null;
        }
    }
}
