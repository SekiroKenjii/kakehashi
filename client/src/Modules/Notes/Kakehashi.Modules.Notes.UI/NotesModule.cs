using System;
using System.Collections.Generic;
using Kakehashi.App.Infrastructure.DependencyInjection;
using Kakehashi.Modules.Notes.Application;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.UI.Infrastructure;
using Kakehashi.Modules.Notes.UI.ViewModels;
using Kakehashi.Modules.Notes.UI.Views;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotesV1 = Kakehashi.Notes.V1;

namespace Kakehashi.Modules.Notes.UI {
  /// <summary>
  /// Composition entry point for the Notes module: registers the application layer, the gRPC
  /// client for its own contract, the adapter behind the gateway port, and the page.
  /// </summary>
  public sealed class NotesModule : IModule {
    public string Name => "Notes";

    public ModuleDescriptor Descriptor { get; } = new(
        "Notes",
        "The reference feature: a full vertical slice from this page to the server's database.",
        IsRequired: false,
        AssignmentId: "notes");

    public void RegisterServices(IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);

      services.AddNotesApplication();

      // The host's helper rather than a hand-rolled channel, so the access token is attached the
      // same way it is everywhere else. A module that wired its own client would work right up
      // until the server started checking.
      services.AddBackendGrpcClient<NotesV1.NotesService.NotesServiceClient>();

      services.TryAddSingleton<INotesGateway, GrpcNotesGateway>();
      services.AddTransient<NotesViewModel>();
      services.AddTransient<NotesPage>();
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems() {
      return [
        new NavigationItem("Notes", "\uE70B", typeof(NotesPage)) { Id = "notes", Group = "Utilities" },
      ];
    }
  }
}
