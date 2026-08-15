using System;
using System.Collections.Generic;
using __ROOT_NAMESPACE__.App.Infrastructure.DependencyInjection;
using __ROOT_NAMESPACE__.Modules.Activity.Application;
using __ROOT_NAMESPACE__.Modules.Activity.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Activity.UI.Infrastructure;
using __ROOT_NAMESPACE__.Modules.Activity.UI.ViewModels;
using __ROOT_NAMESPACE__.Modules.Activity.UI.Views;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActivityV1 = __ROOT_NAMESPACE__.Activity.V1;

namespace __ROOT_NAMESPACE__.Modules.Activity.UI;

/// <summary>
/// Composition entry point for the Activity module: registers the application layer, the gRPC
/// client for its own contract, the adapter behind the gateway port, and the page.
/// </summary>
public sealed class ActivityModule : IModule
{
    public string Name => "Activity";

    public ModuleDescriptor Descriptor { get; } = new(
        "Activity",
        "What has happened to your account, gathered on the server from every device you use.",
        IsRequired: false,
        AssignmentId: "activity");

    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddActivityApplication();

        // The host's helper attaches the access token; without one the server has no account to
        // scope the feed to and answers UNAUTHENTICATED.
        services.AddBackendGrpcClient<ActivityV1.ActivityService.ActivityServiceClient>();

        services.TryAddSingleton<IActivityGateway, GrpcActivityGateway>();
        services.TryAddSingleton<IAccountScreen, AccountScreen>();
        services.AddTransient<ActivityViewModel>();
        services.AddTransient<ActivityPage>();

        // Awake-on-startup: an app update is announced on the first run of a new build, during
        // startup, and a listener created later would miss it.
        services.AddSingleton<IAwakeOnStartup, ActivityReporter>();
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems()
    {
        return [
            new NavigationItem("Activity", "\uF463", typeof(ActivityPage)) { Id = "activity", Group = "Utilities" },
        ];
    }
}
