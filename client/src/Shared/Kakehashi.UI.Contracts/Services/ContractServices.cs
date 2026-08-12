using System;

namespace Kakehashi.UI.Contracts.Services {
  // A deliberately narrow service locator for the shared UI types the XAML runtime constructs
  // itself - behaviors, value converters, markup extensions - which therefore cannot take
  // dependencies through a constructor. Prefer constructor injection everywhere it is possible
  // (pages, view models, services); reach for this only from objects the framework builds with
  // new(), and resolve at the point of use, such as a Behavior in OnAttached.
  public static class ContractServices {
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider
        ?? throw new InvalidOperationException(
            "ContractServices has not been configured. Call ContractServices.Configure(...) during startup.");

    // Call once, at startup, before any XAML that reaches for Provider is loaded.
    public static void Configure(IServiceProvider provider) {
      ArgumentNullException.ThrowIfNull(provider);
      _provider = provider;
    }
  }
}
