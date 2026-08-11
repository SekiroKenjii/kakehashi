using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Notes.Application {
  // Registers the Notes application layer: its message handlers.
  public static class NotesApplicationServiceCollectionExtensions {
    // The assembly that contains the Notes application handlers.
    public static Assembly Assembly =>
        typeof(NotesApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddNotesApplication(this IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);
      services.AddMediator(Assembly);
      return services;
    }
  }
}
