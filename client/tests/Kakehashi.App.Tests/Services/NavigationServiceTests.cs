using System;
using Kakehashi.App.Services.Platform;
using Kakehashi.App.UI;
using Kakehashi.UI.Contracts;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.Services {
  // Only the headless-reachable half: page-key derivation. Navigating (and the detached-module
  // guard) drives a XAML Frame and resolves Page instances, so UI/integration tests cover it.
  public sealed class NavigationServiceTests {
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();
    private readonly IModuleRegistry _moduleRegistry = Substitute.For<IModuleRegistry>();

    private NavigationService CreateService() {
      _moduleRegistry.All.Returns([]);
      return new NavigationService(_services, _moduleRegistry);
    }

    [Fact]
    public void GetPageKey_StripsThePageSuffix() {
      var service = CreateService();

      Assert.Equal("Home", service.GetPageKey(typeof(HomePage)));
      Assert.Equal("Settings", service.GetPageKey(typeof(SettingsPage)));
    }

    [Fact]
    public void GetPageKey_WhenTypeNameLacksPageSuffix_Throws() {
      var service = CreateService();

      Assert.Throws<ArgumentException>(() => service.GetPageKey(typeof(object)));
    }

    [Fact]
    public void NavigateTo_BeforeInitialize_ReturnsFalse() {
      var service = CreateService();

      Assert.False(service.NavigateTo("Home"));
    }
  }
}
