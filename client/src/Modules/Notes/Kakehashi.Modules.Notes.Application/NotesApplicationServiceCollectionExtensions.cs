using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Notes.Application {
  public static class NotesApplicationServiceCollectionExtensions {
    public static Assembly Assembly =>
        typeof(NotesApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddNotesApplication(this IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);
      services.AddMediator(Assembly);
      return services;
    }
  }
}
