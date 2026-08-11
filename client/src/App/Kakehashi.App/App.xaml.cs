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
  // Application composition root. Kept deliberately lean: it builds the host and hands control to the
  // AppOrchestrator, which runs the startup pipeline. Service access for the few
  // framework-instantiated types (XAML behaviors) goes through the static accessors below.
  //
  // The base type is fully qualified because the solution also has a Kakehashi.Application
  // namespace, which otherwise shadows the unqualified Microsoft.UI.Xaml.Application type name.
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

    // The current application instance, typed as App.
    public new static App Current =>
        Microsoft.UI.Xaml.Application.Current as App
        ?? throw new InvalidOperationException("Current application is not App.");

    // The application's service provider. Throws if accessed before the host is built.
    public static IServiceProvider Services =>
        Current._host?.Services
        ?? throw new InvalidOperationException("The host has not been built yet.");

    // The main application window. Throws if accessed before it is created.
    public static WindowEx MainWindow =>
        Current._window
        ?? throw new InvalidOperationException("The main window has not been created yet.");

    // The element registered as the window's title bar.
    public static UIElement AppTitleBar {
      get => Current._appTitleBar
          ?? throw new InvalidOperationException("The app title bar has not been set yet.");
      set => Current._appTitleBar = value;
    }

    // A fresh ISubscription from the container.
    public static ISubscription Subscription => Services.GetRequiredService<ISubscription>();

    // Resolves a UI service.
    //
    // The IUiContractService constraint is what keeps these static accessors from
    // becoming a service locator for the whole app: a type has to opt in before non-UI code can
    // reach it this way. It is a convention the compiler happens to check, not a boundary.
    //
    // TService: The service.
    // Returns: The service.
    // Throws InvalidOperationException: The service is not registered.
    public static TService GetService<TService>() where TService : IUiContractService {
      return Services.GetService(typeof(TService)) is TService service
          ? service
          : throw new InvalidOperationException(
              $"Service of type {typeof(TService)} is not registered.");
    }

    // Every registered service of a type.
    // TService: The service.
    // Returns: The services, possibly none.
    public static IEnumerable<TService> GetServices<TService>() where TService : class {
      return Services.GetServices<TService>();
    }

    // Resolves a view model.
    // TViewModel: The view model.
    // Returns: The view model.
    // Throws InvalidOperationException: The view model is not registered.
    public static TViewModel GetViewModel<TViewModel>() where TViewModel : ViewModel {
      return Services.GetService(typeof(TViewModel)) is TViewModel viewModel
          ? viewModel
          : throw new InvalidOperationException(
              $"View model of type {typeof(TViewModel)} is not registered.");
    }

    // Stops the host on the way out, giving hosted services five seconds to finish.
    //
    // Never throws. The process is leaving either way, and an exception here would replace an
    // orderly exit with a crash dialog over a cleanup nobody was waiting on.
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

    // Records a Task that faulted with nobody awaiting it.
    //
    // Marked observed, so it is written down rather than escalating. The alternative is a process
    // that dies at an arbitrary later moment, during a garbage collection, with a stack that
    // points at the finalizer instead of at the code that failed.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
      Log("Unobserved task exception.", e.Exception);
      e.SetObserved();
    }

    // Records anything that escapes a non-UI thread. Nothing can be done but write it.
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
