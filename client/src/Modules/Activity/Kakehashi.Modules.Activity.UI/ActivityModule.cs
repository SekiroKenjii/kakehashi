using System;
using System.Collections.Generic;
using Kakehashi.App.Infrastructure.DependencyInjection;
using Kakehashi.Modules.Activity.Application;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.UI.Infrastructure;
using Kakehashi.Modules.Activity.UI.ViewModels;
using Kakehashi.Modules.Activity.UI.Views;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActivityV1 = Kakehashi.Activity.V1;

namespace Kakehashi.Modules.Activity.UI {
  public sealed class ActivityModule : IModule {
    public string Name => "Activity";

    public ModuleDescriptor Descriptor { get; } = new(
        "Activity",
        "What has happened to your account, gathered on the server from every device you use.",
        IsRequired: false,
        AssignmentId: "activity");

    public void RegisterServices(IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);

      services.AddActivityApplication();

      // The host's helper rather than a hand-rolled channel, so the access token is attached the
      // same way as everywhere else. Without a token the server has no account to scope the feed
      // to, and answers UNAUTHENTICATED.
      services.AddBackendGrpcClient<ActivityV1.ActivityService.ActivityServiceClient>();

      services.TryAddSingleton<IActivityGateway, GrpcActivityGateway>();
      services.TryAddSingleton<IAccountScreen, AccountScreen>();
      services.AddTransient<ActivityViewModel>();
      services.AddTransient<ActivityPage>();

      // Awake on startup, because what it listens for is announced during startup: an app update is
      // noticed on the first run of a new build, and a listener created later would miss the one
      // moment worth reporting.
      services.AddSingleton<IAwakeOnStartup, ActivityReporter>();
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems() {
      return [
        new NavigationItem("Activity", "\uF463", typeof(ActivityPage)) { Id = "activity", Group = "Utilities" },
      ];
    }
  }
}
