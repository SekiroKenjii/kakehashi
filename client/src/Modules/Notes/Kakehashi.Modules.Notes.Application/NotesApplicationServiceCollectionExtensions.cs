using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Notes.Application {
  /// <summary>Registers the Notes application layer: its message handlers.</summary>
  public static class NotesApplicationServiceCollectionExtensions {
    /// <summary>The assembly that contains the Notes application handlers.</summary>
    public static Assembly Assembly =>
        typeof(NotesApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddNotesApplication(this IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);
      services.AddMediator(Assembly);
      return services;
    }
  }
}
