using System;
using System.IO;
using Kakehashi.App.Composition;
using Kakehashi.App.Core;
using Kakehashi.App.Hosting.Orchestration;
using Kakehashi.App.Infrastructure.DependencyInjection;
using Kakehashi.App.Infrastructure.Observability;
using Kakehashi.App.Services;
using Kakehashi.App.Services.Platform;
using Kakehashi.App.UI;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using AccountV1 = Kakehashi.Account.V1;
using AuthzV1 = Kakehashi.Authz.V1;
using NavigationV1 = Kakehashi.Navigation.V1;

namespace Kakehashi.App.Hosting {
  // The one place composition happens; App.xaml.cs just runs it.
  internal static class AppHost {
    public static IHost Build() {
      var builder = Host.CreateApplicationBuilder();

      builder.Configuration.AddJsonFile(
          Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
          optional: true,
          reloadOnChange: false);
#if DEBUG
      // Gitignored developer overrides (real auth/backend endpoints for the local dev loop). The
      // committed appsettings.json keeps template defaults, so fresh clones start with auth inert.
      builder.Configuration.AddJsonFile(
          Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"),
          optional: true,
          reloadOnChange: false);
#endif

      builder.Logging.AddDebug();

      // A file log beside the debug one: AddDebug alone writes through OutputDebugString, which a
      // debugger sees and nobody else does, so a build handed to anyone but its author kept no
      // record of what went wrong.
      builder.Logging.AddProvider(new FileLoggerProvider(
          builder.Configuration.GetValue("Logging:File:MinimumLevel", LogLevel.Information)));

      builder.Services.AddObservability(builder.Configuration);
      builder.Services.AddBackendInfrastructure(builder.Configuration);

      AddFundamentals(builder.Services);
      AddPlatformServices(builder.Services);
      AddViewsAndViewModels(builder.Services);
      AddOrchestrators(builder.Services);
      AddModules(builder.Services);

      return builder.Build();
    }

    private static void AddPlatformServices(IServiceCollection services) {
      services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
      services.AddSingleton<IModuleRegistry, ModuleRegistry>();

      // The authorization clients are the host's, not a module's: what they feed is the registry
      // and the two administration screens, which govern every module. A feature module that
      // governed the others would be one reaching across the boundary the architecture tests hold.
      services.AddBackendGrpcClient<AuthzV1.AuthzService.AuthzServiceClient>();
      services.AddSingleton<PermissionService>();
      services.AddSingleton<IPermissionService>(
          provider => provider.GetRequiredService<PermissionService>());

      // The navigation clients are the host's for the same reason: the pane belongs to the shell,
      // and the screen that arranges it governs every module's place in it.
      services.AddBackendGrpcClient<NavigationV1.NavigationService.NavigationServiceClient>();
      services.AddSingleton<INavigationLayoutService, NavigationLayoutService>();

      services.AddBackendGrpcClient<
          NavigationV1.NavigationAdminService.NavigationAdminServiceClient>();
      services.AddSingleton<INavigationAdminService, NavigationAdminService>();

      services.AddBackendGrpcClient<AuthzV1.AuthzAdminService.AuthzAdminServiceClient>();
      services.AddBackendGrpcClient<AccountV1.AccountAdminService.AccountAdminServiceClient>();
      services.AddSingleton<IAccessAdminService, AccessAdminService>();
      // The activity log doubles as an IAwakeOnStartup so it records events (the startup sign-in,
      // an app update) that happen before any page exists.
      services.AddSingleton<AppActivityLog>();
      services.AddSingleton<IAwakeOnStartup>(
          provider => provider.GetRequiredService<AppActivityLog>());
      services.AddSingleton<INavigationService, NavigationService>();
      services.AddSingleton<IThemeService, ThemeService>();
      services.AddSingleton<IDialogService, DialogService>();
      services.AddSingleton<IClipboardService, ClipboardService>();
      services.AddSingleton<IFileSaveService, FileSaveService>();
      services.AddSingleton<INotificationService, NotificationService>();
      services.AddSingleton<IMainWindowProvider, MainWindowProvider>();
      services.AddSingleton<IShellOverlay, ShellOverlayService>();
    }

    private static void AddFundamentals(IServiceCollection services) {
      services.AddTransient<ISubscription, Subscription>();
    }

    private static void AddViewsAndViewModels(IServiceCollection services) {
      services.AddTransient<MainWindow>();
      services.AddTransient<SplashWindow>();
      services.AddTransient<ShellPage>();
      services.AddTransient<HomePage>();
      services.AddTransient<SettingsPage>();
      services.AddTransient<RolePermissionsPage>();
      services.AddTransient<NavigationLayoutViewModel>();
      services.AddTransient<NavigationLayoutPage>();
      services.AddTransient<UsersPage>();

      services.AddTransient<ShellViewModel>();
      services.AddTransient<SplashViewModel>();
      services.AddTransient<HomeViewModel>();
      services.AddTransient<SettingsViewModel>();
      services.AddTransient<RolePermissionsViewModel>();
      services.AddTransient<UsersViewModel>();
    }

    private static void AddOrchestrators(IServiceCollection services) {
      services.AddSingleton<StartupContext>();
      services.AddSingleton<AppOrchestrator>();
      services.AddSingleton<IStartupOrchestrator, SplashOrchestrator>();
      services.AddSingleton<IStartupOrchestrator, AuthenticationOrchestrator>();
      services.AddSingleton<IStartupOrchestrator, PermissionOrchestrator>();
      services.AddSingleton<IStartupOrchestrator, ShellOrchestrator>();
      services.AddSingleton<IStartupOrchestrator, ThemeOrchestrator>();
      services.AddSingleton<IStartupOrchestrator, ActivationOrchestrator>();
    }

    private static void AddModules(IServiceCollection services) {
      foreach (var module in ModuleCatalog.Modules) {
        services.AddSingleton(module);
        module.RegisterServices(services);
      }
    }
  }
}
