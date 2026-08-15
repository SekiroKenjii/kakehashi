using System;
using System.Reflection;
using __ROOT_NAMESPACE__.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.Modules.Notes.Application;

/// <summary>Registers the Notes application layer: its message handlers.</summary>
public static class NotesApplicationServiceCollectionExtensions
{
    /// <summary>The assembly that contains the Notes application handlers.</summary>
    public static Assembly Assembly =>
        typeof(NotesApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddNotesApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMediator(Assembly);

        return services;
    }
}
