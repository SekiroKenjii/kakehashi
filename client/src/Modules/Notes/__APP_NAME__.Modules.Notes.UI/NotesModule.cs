using System;
using System.Collections.Generic;
using __ROOT_NAMESPACE__.App.Infrastructure.DependencyInjection;
using __ROOT_NAMESPACE__.Modules.Notes.Application;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Notes.UI.Infrastructure;
using __ROOT_NAMESPACE__.Modules.Notes.UI.ViewModels;
using __ROOT_NAMESPACE__.Modules.Notes.UI.Views;
using __ROOT_NAMESPACE__.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotesV1 = __ROOT_NAMESPACE__.Notes.V1;

namespace __ROOT_NAMESPACE__.Modules.Notes.UI;

/// <summary>
/// Composition entry point for the Notes module: registers the application layer, the gRPC
/// client for its own contract, the adapter behind the gateway port, and the page.
/// </summary>
public sealed class NotesModule : IModule
{
    public string Name => "Notes";

    public ModuleDescriptor Descriptor { get; } = new(
        "Notes",
        "The reference feature: a full vertical slice from this page to the server's database.",
        IsRequired: false,
        AssignmentId: "notes");

    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNotesApplication();

        // The host's helper, not a hand-rolled channel, so the token is attached as it is everywhere
        // else: a module wiring its own client works right up until the server starts checking.
        services.AddBackendGrpcClient<NotesV1.NotesService.NotesServiceClient>();

        services.TryAddSingleton<INotesGateway, GrpcNotesGateway>();
        services.AddTransient<IGettingStartedStep, NotesGettingStartedStep>();
        services.AddTransient<NotesViewModel>();
        services.AddTransient<NotesPage>();
        // kakehashi:module-page-services:begin
        // kakehashi:module-page-services:end
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems()
    {
        return [
            new NavigationItem("Notes", "\uE70B", typeof(NotesPage)) { Id = "notes", Group = "Utilities" },
            // kakehashi:module-page-navigation:begin
            // kakehashi:module-page-navigation:end
        ];
    }
}
