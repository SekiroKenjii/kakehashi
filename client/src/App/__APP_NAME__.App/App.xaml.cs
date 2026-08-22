using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.App.Hosting;
using __ROOT_NAMESPACE__.App.Hosting.Orchestration;
using __ROOT_NAMESPACE__.App.UI;
using __ROOT_NAMESPACE__.PluginSdk.Xaml;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace __ROOT_NAMESPACE__.App;

/// <summary>
/// Application composition root: builds the host, hands control to the <see cref="AppOrchestrator"/>
/// startup pipeline, and exposes static service accessors for the few framework-instantiated XAML
/// types.
/// </summary>
/// <remarks>
/// The base type is fully qualified because the solution also has a <c>__ROOT_NAMESPACE__.Application</c>
/// namespace, which otherwise shadows the unqualified <see cref="Microsoft.UI.Xaml.Application"/> type name.
/// </remarks>
public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly PluginXamlHost _pluginXaml = new();

    private IHost? _host;
    private WindowEx? _window;
    private UIElement? _appTitleBar;

    public App()
    {
        // Before InitializeComponent and never later: the framework asks for its resource manager
        // once per UI thread as it starts, which is earlier than OnLaunched.
        _pluginXaml.Attach(this);
        InitializeComponent();

        UnhandledException += OnUnhandledException;

        // The two channels the XAML handler above does not cover: a faulted fire-and-forget Task
        // surfaces only at finalization, and exceptions off the UI thread never reach XAML.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    public new static App Current =>
        Microsoft.UI.Xaml.Application.Current as App
        ?? throw new InvalidOperationException("Current application is not App.");

    /// <summary>Throws if accessed before the host is built.</summary>
    public static IServiceProvider Services =>
        Current._host?.Services
        ?? throw new InvalidOperationException("The host has not been built yet.");

    /// <summary>Throws if accessed before the window is created.</summary>
    public static WindowEx MainWindow =>
        Current._window
        ?? throw new InvalidOperationException("The main window has not been created yet.");

    public static UIElement AppTitleBar
    {
        get => Current._appTitleBar
            ?? throw new InvalidOperationException("The app title bar has not been set yet.");
        set => Current._appTitleBar = value;
    }

    /// <summary>Resolves a new <see cref="ISubscription"/> instance on every access.</summary>
    public static ISubscription Subscription => Services.GetRequiredService<ISubscription>();

    /// <summary>
    /// The <see cref="IUiContractService"/> constraint is a compile-time convention marking types
    /// intended for this static accessor; throws when the service is not registered.
    /// </summary>
    public static TService GetService<TService>() where TService : IUiContractService
    {
        return Services.GetService(typeof(TService)) is TService service
            ? service
            : throw new InvalidOperationException(
                $"Service of type {typeof(TService)} is not registered.");
    }

    public static IEnumerable<TService> GetServices<TService>() where TService : class
    {
        return Services.GetServices<TService>();
    }

    /// <summary>Throws when the view model is not registered.</summary>
    public static TViewModel GetViewModel<TViewModel>() where TViewModel : ViewModel
    {
        return Services.GetService(typeof(TViewModel)) is TViewModel viewModel
            ? viewModel
            : throw new InvalidOperationException(
                $"View model of type {typeof(TViewModel)} is not registered.");
    }

    /// <summary>
    /// Stops and disposes the host with a 5-second timeout; failures are swallowed because shutdown
    /// during application exit is best-effort.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        var host = Current._host;

        if (host is null)
        {
            return;
        }

        Current._host = null;
        try
        {
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort shutdown: never let teardown crash the exit path.
        }
        finally
        {
            host.Dispose();
        }
    }

    internal void SetMainWindow(WindowEx window)
    {
        _window = window;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _host = AppHost.Build(_pluginXaml);

            // Make the container available to the few shared UI types the XAML runtime constructs via
            // new() (e.g. NavigationViewHeaderBehavior), which cannot use constructor injection.
            ContractServices.Configure(_host.Services);

            await _host.StartAsync();

            var awakeOnStartupServices = _host.Services.GetServices<IAwakeOnStartup>();
            await _host.Services
                .GetRequiredService<AppOrchestrator>()
                .StartAsync(awakeOnStartupServices);
        }
        catch (OperationCanceledException)
        {
            // Startup was cancelled deliberately (e.g. the user declined to sign in and chose to quit).
            // This is a normal exit, not a failure, so shut down quietly instead of showing the error window.
            await ShutdownAsync();
            Current.Exit();
        }
        catch (Exception ex)
        {
            ExceptionWindow.ShowException(ex);
        }
    }

    /// <summary>Records a Task that faulted with nobody awaiting it.</summary>
    /// <remarks>
    /// SetObserved stops the exception escalating at finalization, which would kill the process at
    /// an arbitrary later moment with a stack pointing at the finalizer instead of the code that
    /// failed.
    /// </remarks>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    /// <summary>Logs anything that escapes a non-UI thread; the runtime terminates regardless.</summary>
    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        Log("Unhandled exception on a background thread.", e.ExceptionObject as Exception);
    }

    private void Log(string message, Exception? exception)
    {
        try
        {
            _host?.Services.GetService<ILogger<App>>()?.LogError(exception, "{Message}", message);
        }
        catch (Exception)
        {
            // Ignore logging failures while handling an exception.
        }
    }

    private void OnUnhandledException(
        object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        try
        {
            _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled exception.");
        }
        catch (Exception)
        {
            // Ignore logging failures while handling an exception.
        }

        ExceptionWindow.ShowException(e.Exception);
    }
}
