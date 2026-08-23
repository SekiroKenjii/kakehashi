using System;
using System.Collections.Generic;
using System.Linq;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>
/// Lets a plugin add its own services, and refuses one that reaches any other way.
/// </summary>
/// <remarks>
/// A module's registration is additive, and there are three ways to break that. Removing an entry
/// is caught by comparing against a snapshot. Registering a service type the host already provides
/// is caught by name, because removal is not required to substitute one — the container resolves
/// the last descriptor and a plugin registers last. Throwing is caught by the filter.
/// <para>
/// Any of the three rolls the whole registration back rather than honouring part of it. Here rather
/// than in the composition root so it can be tested against a real collection, which is the only
/// way to know the container agrees with what the rule assumes about it.
/// </para>
/// </remarks>
public static class PluginRegistration
{
    public static Result Add(IServiceCollection services, IModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);
        var before = services.ToArray();
        var provided = before
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();

        try
        {
            module.RegisterServices(services);
        }
        catch (Exception exception)
        {
            return Undo(services, before, PluginLoadErrors.Threw(nameof(IModule.RegisterServices), exception));
        }
        var after = new HashSet<ServiceDescriptor>(services);

        if (!before.All(after.Contains))
        {
            return Undo(services, before, PluginLoadErrors.RegistrationRemovedServices(module.Name));
        }

        var shadowed = services
            .Where(descriptor => !before.Contains(descriptor))
            .FirstOrDefault(descriptor => provided.Contains(descriptor.ServiceType));

        return shadowed is null
            ? Result.Success()
            : Undo(
                services,
                before,
                PluginLoadErrors.RegistrationShadowedService(module.Name, shadowed.ServiceType.Name));
    }

    private static Result Undo(
        IServiceCollection services, IReadOnlyList<ServiceDescriptor> before, Error reason)
    {
        services.Clear();

        foreach (var descriptor in before)
        {
            services.Add(descriptor);
        }

        return Result.Failure(reason);
    }
}
