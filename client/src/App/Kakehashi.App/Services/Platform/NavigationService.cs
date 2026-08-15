using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Kakehashi.App.Infrastructure.Observability;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.Services.Platform;

/// <summary>
/// Shows pages in the shell's content <see cref="Frame"/>. Pages resolve from the container with
/// constructor injection, so navigation sets <see cref="Frame.Content"/> directly and keeps its
/// own back stack; pages start their load on <c>Loaded</c>, never <c>OnNavigatedTo</c>:
/// docs/adr/0011-pages-load-on-loaded-not-onnavigatedto.md
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly Subject<NavigationEvent> _navigated = new();
    private readonly Dictionary<string, Type> _registry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pageKeyModules = new(StringComparer.Ordinal);
    private readonly Stack<NavigationEntry> _backStack = new();
    private Frame? _frame;
    private NavigationEntry? _current;

    public NavigationService(IServiceProvider services, IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        _services = services;
        _moduleRegistry = moduleRegistry;

        // Module pages stay registered even when detached; navigability is decided per call from
        // the attachment state, so re-attaching needs no re-registration.
        foreach (var module in moduleRegistry.All)
        {
            foreach (var item in module.GetNavigationItems())
            {
                Register(item.PageType);
                _pageKeyModules[GetPageKey(item.PageType)] = module.Name;
            }
        }
    }

    public IObservable<NavigationEvent> OnNavigated => _navigated.AsObservable();

    public bool CanGoBack => _backStack.Count > 0;

    public void Initialize(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
    }

    public void Register<TPage>() where TPage : Page
    {
        Register(typeof(TPage));
    }

    public void Register(params ReadOnlySpan<Type> pageTypes)
    {
        foreach (var pageType in pageTypes)
        {
            _registry[GetPageKey(pageType)] = pageType;
        }
    }

    public string GetPageKey(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        const string suffix = "Page";
        string pageClassName = pageType.Name;
        if (!pageClassName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Page type name '{pageClassName}' must end with 'Page' to derive a navigation key.",
                nameof(pageType));
        }

        return pageClassName[..^suffix.Length];
    }

    public bool NavigateTo(string pageKey, params ReadOnlySpan<object> args)
    {
        if (_frame is null || !_registry.TryGetValue(pageKey, out var pageType))
        {
            return false;
        }

        // Pages of detached modules are not navigable. This is the single choke point, so it also
        // covers flyout actions, deep links and programmatic navigation.
        if (IsDetached(pageKey))
        {
            return false;
        }

        if (Resolve(pageType) is not { } page)
        {
            return false;
        }

        using var activity = Telemetry.ActivitySource.StartActivity("Navigation.NavigateTo");
        activity?.SetTag("navigation.page", pageType.Name);

        if (_current is not null)
        {
            _backStack.Push(_current);
        }

        _current = new NavigationEntry(pageKey, args.ToArray());
        _frame.Content = page;
        _navigated.OnNext(new NavigationEvent(pageKey, pageType, page, _current.Parameters));
        return true;
    }

    public void GoBack()
    {
        if (_frame is null)
        {
            return;
        }

        while (_backStack.Count > 0)
        {
            var entry = _backStack.Pop();
            // Entries whose module was detached after they were pushed are skipped.
            if (IsDetached(entry.PageKey))
            {
                continue;
            }

            if (Resolve(_registry[entry.PageKey]) is not { } page)
            {
                return;
            }

            _current = entry;
            _frame.Content = page;
            _navigated.OnNext(
                new NavigationEvent(entry.PageKey, page.GetType(), page, entry.Parameters));
            return;
        }
    }

    public void ClearBackStack()
    {
        _backStack.Clear();
    }

    private Page? Resolve(Type pageType)
    {
        // Pages are registered in the container (host pages in AppHost, module pages via IModule), so
        // they are resolved with constructor injection. An unregistered page yields null (no navigation).
        return _services.GetService(pageType) as Page;
    }

    private bool IsDetached(string pageKey)
    {
        return _pageKeyModules.TryGetValue(pageKey, out var moduleName)
            && !_moduleRegistry.IsAttached(moduleName);
    }

    private sealed record NavigationEntry(string PageKey, object[] Parameters);
}
