using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>
/// Lets a plugin add its own services, and refuses one that reaches any other way.
/// </summary>
/// <remarks>
/// A module's registration is additive, and there are four ways to break that. Removing an entry is
/// caught by comparing against a snapshot. Registering a service the host already provides is caught
/// by type, because removal is not required to substitute one — the container resolves the last
/// descriptor and a plugin registers last. Reaching into the options pipeline is caught separately,
/// because post-configuration is a seam the host never registers into and so cannot be shadowing.
/// Throwing is caught by the filter.
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

        var added = services.Where(descriptor => !before.Contains(descriptor));
        var plugin = module.GetType().Assembly;

        foreach (var descriptor in added)
        {
            if (Provides(provided, descriptor.ServiceType))
            {
                return Undo(
                    services,
                    before,
                    PluginLoadErrors.RegistrationShadowedService(module.Name, Named(descriptor.ServiceType)));
            }

            if (ReachesHostOptions(descriptor.ServiceType, plugin))
            {
                return Undo(
                    services,
                    before,
                    PluginLoadErrors.RegistrationReachedHostOptions(module.Name, Named(descriptor.ServiceType)));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Whether the host already answers a request for this service.
    /// </summary>
    /// <remarks>
    /// Asymmetric on purpose. An open generic the host registered answers every closed request of
    /// it, so <c>IOptions&lt;BackendOptions&gt;</c> is the host's even though only
    /// <c>IOptions&lt;&gt;</c> was registered. The reverse is not true: a plugin registering
    /// <c>IConfigureOptions</c> for its own options type is not reaching for the host's, and
    /// normalising both sides would refuse it.
    /// </remarks>
    private static bool Provides(HashSet<Type> provided, Type serviceType)
    {
        return provided.Contains(serviceType)
            || (serviceType.IsConstructedGenericType
                && provided.Contains(serviceType.GetGenericTypeDefinition()));
    }

    /// <summary>
    /// Whether this registration would configure options belonging to somebody else.
    /// </summary>
    /// <remarks>
    /// The host registers no post-configuration of its own, so nothing above catches it — and post-
    /// configuration runs after the host's own, which is substitution without shadowing. Narrowed to
    /// the options namespace rather than every generic over a host type, because a handler for a
    /// host notification is the sanctioned way modules collaborate.
    /// </remarks>
    private static bool ReachesHostOptions(Type serviceType, Assembly plugin)
    {
        return serviceType.IsConstructedGenericType
            && serviceType.Namespace == "Microsoft.Extensions.Options"
            && serviceType.GenericTypeArguments.Any(argument => argument.Assembly != plugin);
    }

    private static string Named(Type serviceType)
    {
        return serviceType.IsConstructedGenericType
            ? $"{serviceType.Name[..serviceType.Name.IndexOf('`', StringComparison.Ordinal)]}<{serviceType.GenericTypeArguments[0].Name}>"
            : serviceType.Name;
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
