using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kakehashi.App.Hosting;
using Kakehashi.App.Hosting.Orchestration;
using Kakehashi.App.UI;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace Kakehashi.App {
  /// <summary>
  /// Application composition root. Kept deliberately lean: it builds the host and hands control to the
  /// <see cref="AppOrchestrator"/>, which runs the startup pipeline. Service access for the few
  /// framework-instantiated types (XAML behaviors) goes through the static accessors below.
  /// </summary>
  /// <remarks>
  /// The base type is fully qualified because the solution also has a <c>Kakehashi.Application</c>
  /// namespace, which otherwise shadows the unqualified <see cref="Microsoft.UI.Xaml.Application"/> type name.
  /// </remarks>
  public sealed partial class App : Microsoft.UI.Xaml.Application {
    private IHost? _host;
    private WindowEx? _window;
    private UIElement? _appTitleBar;

    public App() {
      InitializeComponent();

      UnhandledException += OnUnhandledException;

      // The two channels the XAML handler above does not cover. A fire-and-forget Task that
      // faults never reaches it — the failure is finalized quietly and the app carries on with a
      // page that silently did not load, which is the hardest kind of bug to be told about.
      TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
      AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    /// <summary>The current application instance, typed as <see cref="App"/>.</summary>
    public new static App Current =>
        Microsoft.UI.Xaml.Application.Current as App
        ?? throw new InvalidOperationException("Current application is not App.");

    /// <summary>The application's service provider. Throws if accessed before the host is built.</summary>
    public static IServiceProvider Services =>
        Current._host?.Services
        ?? throw new InvalidOperationException("The host has not been built yet.");

    /// <summary>The main application window. Throws if accessed before it is created.</summary>
    public static WindowEx MainWindow =>
        Current._window
        ?? throw new InvalidOperationException("The main window has not been created yet.");

    /// <summary>The element registered as the window's title bar.</summary>
    public static UIElement AppTitleBar {
      get => Current._appTitleBar
          ?? throw new InvalidOperationException("The app title bar has not been set yet.");
      set => Current._appTitleBar = value;
    }

    /// <summary>
    /// Gets the a new instance of <see cref="ISubscription"/> from the service provider.
    /// </summary>
    public static ISubscription Subscription => Services.GetRequiredService<ISubscription>();

    /// <summary>
    /// Gets a UI service from the container. Services accessed via this method must implement <see cref="IUiContractService"/>
    /// to indicate that they are designed for use in the UI layer and can be safely accessed via this static accessor. This is a compile-time convention only, but helps to prevent accidental misuse of the static service accessors by non-UI code.
    /// </summary>
    /// <typeparam name="TService">The type of the UI service that implements <see cref="IUiContractService"/>.</typeparam>
    /// <returns>The requested service instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the service is not registered.</exception>
    public static TService GetService<TService>() where TService : IUiContractService {
      return Services.GetService(typeof(TService)) is TService service
          ? service
          : throw new InvalidOperationException(
              $"Service of type {typeof(TService)} is not registered.");
    }

    /// <summary>
    /// Gets all registered services of the specified type. Services accessed via this method must implement <see cref="IUiContractService"/> to indicate that they are designed for use in the UI layer and can be safely accessed via this static accessor. This is a compile-time convention only, but helps to prevent accidental misuse of the static service accessors by non-UI code.
    /// </summary>
    /// <typeparam name="TService">The type of the UI service that implements <see cref="IUiContractService"/>.</typeparam>
    /// <returns>An enumerable of the requested service instances.</returns>
    public static IEnumerable<TService> GetServices<TService>() where TService : class {
      return Services.GetServices<TService>();
    }

    /// <summary>
    /// Gets a view model from the container. View models accessed via this method must implement <see cref="ViewModel"/> to indicate that they are designed for use as view models and can be safely accessed via this static accessor. This is a compile-time convention only, but helps to prevent accidental misuse of the static service accessors by non-UI code.
    /// </summary>
    /// <typeparam name="TViewModel">The type of the view model that implements <see cref="ViewModel"/>.</typeparam>
    /// <returns>The requested view model instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the view model is not registered.</exception>
    public static TViewModel GetViewModel<TViewModel>() where TViewModel : ViewModel {
      return Services.GetService(typeof(TViewModel)) is TViewModel viewModel
          ? viewModel
          : throw new InvalidOperationException(
              $"View model of type {typeof(TViewModel)} is not registered.");
    }

    /// <summary>
    /// Shuts down the application host, allowing for graceful cleanup of resources. This should be called during application exit to ensure that all hosted services are properly stopped and disposed. The method attempts to stop the host within a 5-second timeout, but will not throw if shutdown fails, as it is a best-effort operation during application teardown.
    /// </summary>
    /// <returns>A task that represents the asynchronous shutdown operation.</returns>
    public static async Task ShutdownAsync() {
      var host = Current._host;
      if (host is null) {
        return;
      }

      Current._host = null;
      try {
        await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
      } catch (Exception) {
        // Best-effort shutdown: never let teardown crash the exit path.
      } finally {
        host.Dispose();
      }
    }

    internal void SetMainWindow(WindowEx window) {
      _window = window;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args) {
      try {
        _host = AppHost.Build();

        // Make the container available to the few shared UI types the XAML runtime constructs via
        // new() (e.g. NavigationViewHeaderBehavior), which cannot use constructor injection.
        ContractServices.Configure(_host.Services);

        await _host.StartAsync();

        var awakeOnStartupServices = _host.Services.GetServices<IAwakeOnStartup>();
        await _host.Services.GetRequiredService<AppOrchestrator>().StartAsync(awakeOnStartupServices);
      } catch (OperationCanceledException) {
        // Startup was cancelled deliberately (e.g. the user declined to sign in and chose to quit).
        // This is a normal exit, not a failure, so shut down quietly instead of showing the error window.
        await ShutdownAsync();
        Current.Exit();
      } catch (Exception ex) {
        ExceptionWindow.ShowException(ex);
      }
    }

    /// <summary>Records a Task that faulted with nobody awaiting it.</summary>
    /// <remarks>
    /// Marked observed, so it is written down rather than escalating. The alternative is a process
    /// that dies at an arbitrary later moment, during a garbage collection, with a stack that
    /// points at the finalizer instead of at the code that failed.
    /// </remarks>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
      Log("Unobserved task exception.", e.Exception);
      e.SetObserved();
    }

    /// <summary>Records anything that escapes a non-UI thread. Nothing can be done but write it.</summary>
    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e) {
      Log("Unhandled exception on a background thread.", e.ExceptionObject as Exception);
    }

    private void Log(string message, Exception? exception) {
      try {
        _host?.Services.GetService<ILogger<App>>()?.LogError(exception, "{Message}", message);
      } catch (Exception) {
        // Ignore logging failures while handling an exception.
      }
    }

    private void OnUnhandledException(
        object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) {
      e.Handled = true;

      try {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled exception.");
      } catch (Exception) {
        // Ignore logging failures while handling an exception.
      }

      ExceptionWindow.ShowException(e.Exception);
    }
  }
}
