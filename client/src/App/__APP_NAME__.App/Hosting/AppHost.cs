using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using __ROOT_NAMESPACE__.App.Composition;
using __ROOT_NAMESPACE__.App.Core;
using __ROOT_NAMESPACE__.App.Hosting.Orchestration;
using __ROOT_NAMESPACE__.App.Infrastructure.DependencyInjection;
using __ROOT_NAMESPACE__.App.Infrastructure.Observability;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.App.Services.Platform;
using __ROOT_NAMESPACE__.App.UI;
using __ROOT_NAMESPACE__.PluginSdk.Xaml;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using AccountV1 = __ROOT_NAMESPACE__.Account.V1;
using AuthzV1 = __ROOT_NAMESPACE__.Authz.V1;
using NavigationV1 = __ROOT_NAMESPACE__.Navigation.V1;

namespace __ROOT_NAMESPACE__.App.Hosting;

/// <summary>
/// Builds the application's <see cref="IHost"/>: configuration, logging, observability, the backend
/// client, the platform services, the windows/pages/view models, the startup orchestrators, and the
/// feature modules. This is the one place composition happens; <c>App.xaml.cs</c> just runs it.
/// </summary>
internal static class AppHost
{
    public static IHost Build(PluginXamlHost pluginXaml)
    {
        ArgumentNullException.ThrowIfNull(pluginXaml);
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

        // A file log beside the debug one: AddDebug writes through OutputDebugString, which only an
        // attached debugger sees, so the file is the only record on machines without one.
        builder.Logging.AddProvider(new FileLoggerProvider(
            builder.Configuration.GetValue("Logging:File:MinimumLevel", LogLevel.Information)));

        builder.Services.AddObservability(builder.Configuration);
        builder.Services.AddBackendInfrastructure(builder.Configuration);

        AddFundamentals(builder.Services);
        AddPlatformServices(builder.Services);
        AddViewsAndViewModels(builder.Services);
        AddOrchestrators(builder.Services);
        AddModules(builder.Services, LoadPlugins(builder, pluginXaml));

        return builder.Build();
    }

    private static void AddPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();

        // The host's clients, not a module's: they feed the registry and the two administration
        // screens, which govern every module — a role that would couple a feature module across.
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
        services.AddSingleton<IAccentService, AccentService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFileSaveService, FileSaveService>();
        services.AddSingleton<IFileOpenService, FileOpenService>();

        // A generated project references the assemblies beside this executable, so it compiles
        // against exactly the ones it will be loaded next to.
        services.AddSingleton(_ => new PluginScaffolder(AppContext.BaseDirectory));

        // A build that cannot vouch for itself vouches for nothing: every package is then
        // unofficial, and every install is asked about.
        services.AddSingleton(_ => new PluginInstaller(
            PluginPaths.Default, PluginTrust.PublisherOf(Environment.ProcessPath ?? string.Empty)));
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IMainWindowProvider, MainWindowProvider>();
        services.AddSingleton<IShellOverlay, ShellOverlayService>();
    }

    private static void AddFundamentals(IServiceCollection services)
    {
        services.AddTransient<ISubscription, Subscription>();
        services.AddTransient(typeof(IStateManager<>), typeof(StateManager<>));
    }

    private static void AddViewsAndViewModels(IServiceCollection services)
    {
        services.AddTransient<MainWindow>();
        services.AddTransient<SplashWindow>();
        services.AddTransient<ShellPage>();
        services.AddTransient<HomePage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<RolePermissionsPage>();
        services.AddTransient<NavigationLayoutViewModel>();
        services.AddTransient<NavigationLayoutPage>();
        services.AddTransient<UsersPage>();
        services.AddTransient<PluginsPage>();

        services.AddTransient<ShellViewModel>();
        services.AddTransient<SplashViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<RolePermissionsViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<PluginsViewModel>();
    }

    private static void AddOrchestrators(IServiceCollection services)
    {
        services.AddSingleton<StartupContext>();
        services.AddSingleton<AppOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, AccentOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, SplashOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, AuthenticationOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, PermissionOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, ShellOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, ThemeOrchestrator>();
        services.AddSingleton<IStartupOrchestrator, ActivationOrchestrator>();
    }

    private static void AddModules(IServiceCollection services, PluginLoadResult plugins)
    {
        foreach (var module in ModuleCatalog.Modules)
        {
            services.AddSingleton(module);
            module.RegisterServices(services);
        }
        services.AddSingleton(plugins.Catalog);

        foreach (var module in plugins.Modules)
        {
            if (!TryRegisterPlugin(services, module))
            {
                plugins.Catalog.AddFault(
                    module.Name, string.Empty, PluginLoadErrors.RegistrationRemovedServices(module.Name));

                continue;
            }
            services.AddSingleton(module);
        }
    }

    /// <summary>
    /// Lets a plugin add its own services, and refuses one that takes anything away.
    /// </summary>
    /// <remarks>
    /// A module's registration is additive. Removing or replacing what the host registered would
    /// let a plugin substitute its own navigation service, its own token store, its own anything —
    /// so the collection is snapshotted, and a registration that dropped an entry is rolled back
    /// whole rather than partly honoured.
    /// </remarks>
    private static bool TryRegisterPlugin(IServiceCollection services, IModule module)
    {
        var before = services.ToArray();
        module.RegisterServices(services);
        var after = new HashSet<ServiceDescriptor>(services);

        if (before.All(after.Contains))
        {
            return true;
        }
        services.Clear();

        foreach (var descriptor in before)
        {
            services.Add(descriptor);
        }

        return false;
    }

    /// <summary>
    /// Brings installed plugins into this composition, or none when the deployment turned them off.
    /// </summary>
    /// <remarks>
    /// Here rather than later because a module's services have to be registered while the
    /// collection is still open, which is also why installing one takes effect at the next launch.
    /// </remarks>
    private static PluginLoadResult LoadPlugins(
        HostApplicationBuilder builder, PluginXamlHost pluginXaml)
    {
        var options = new PluginOptions();
        builder.Configuration
            .GetSection(PluginOptions.SectionName)
            .Bind(options);

        if (!options.Enabled)
        {
            return new PluginLoadResult([], new PluginCatalog());
        }
        var declared = ModuleCatalog.Modules
            .SelectMany(module => module.GetNavigationItems())
            .Concat(HostNavigation.Items);
        var reserved = PluginLoader.PageKeysOf(declared);

        return PluginLoader.LoadAll(PluginPaths.Default, pluginXaml, reserved);
    }
}
