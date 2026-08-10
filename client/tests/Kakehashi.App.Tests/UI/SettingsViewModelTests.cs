using Kakehashi.App.UI;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.UI {
  /// <summary>
  /// Unit tests for <see cref="SettingsViewModel"/>: the theme ↔ 0/1/2 index mapping read on
  /// construction and applied through <see cref="IThemeService"/> when the index changes.
  /// </summary>
  public sealed class SettingsViewModelTests {
    private readonly IThemeService _theme = Substitute.For<IThemeService>();

    [Fact]
    public void Constructor_MapsCurrentThemeToIndex() {
      _theme.Theme.Returns(ElementTheme.Dark);
      Assert.Equal(2, new SettingsViewModel(_theme).ThemeIndex);

      _theme.Theme.Returns(ElementTheme.Light);
      Assert.Equal(1, new SettingsViewModel(_theme).ThemeIndex);

      _theme.Theme.Returns(ElementTheme.Default);
      Assert.Equal(0, new SettingsViewModel(_theme).ThemeIndex);
    }

    [Fact]
    public void ThemeIndexChange_AppliesMappedTheme() {
      // Starts at index 0 (theme defaults to ElementTheme.Default).
      var viewModel = new SettingsViewModel(_theme);

      viewModel.ThemeIndex = 2;
      viewModel.ThemeIndex = 1;
      viewModel.ThemeIndex = 0;

      _theme.Received(1).SetTheme(ElementTheme.Dark);
      _theme.Received(1).SetTheme(ElementTheme.Light);
      _theme.Received(1).SetTheme(ElementTheme.Default);
    }
  }
}
