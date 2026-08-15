using System;

namespace __ROOT_NAMESPACE__.UI.Contracts.Services;

/// <summary>
/// A minimal service-provider accessor for the few shared UI types that the XAML runtime constructs
/// itself (behaviors, value converters, markup extensions) and therefore cannot receive dependencies
/// through constructor injection. The host configures it once at startup with the application's
/// <see cref="IServiceProvider"/>; those types then resolve their (typically singleton) services from
/// <see cref="Provider"/> at the point they need them - for example a <c>Behavior</c> in
/// <c>OnAttached</c>.
/// </summary>
/// <remarks>
/// This is a deliberately narrow service locator. Prefer constructor injection everywhere it is
/// possible (pages, view models, services); reach for this only from objects the framework
/// instantiates via <c>new()</c>, where injection is not an option.
/// </remarks>
public static class ContractServices
{
    private static IServiceProvider? _provider;

    /// <summary>The configured service provider.</summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed before <see cref="Configure"/>.</exception>
    public static IServiceProvider Provider =>
        _provider
        ?? throw new InvalidOperationException(
            "ContractServices has not been configured. Call ContractServices.Configure(...) during startup.");

    /// <summary>Binds the accessor to the application's service provider. Call once, at startup.</summary>
    public static void Configure(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }
}
