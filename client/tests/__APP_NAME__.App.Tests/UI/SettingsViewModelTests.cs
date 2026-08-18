using __ROOT_NAMESPACE__.App.UI;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;
using NSubstitute;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.UI;

/// <summary>
/// Unit tests for <see cref="SettingsViewModel"/>: the theme ↔ 0/1/2 and accent ↔ 0/1 index
/// mappings read on construction and applied through their services when an index changes.
/// </summary>
public sealed class SettingsViewModelTests
{
    private readonly IThemeService _theme = Substitute.For<IThemeService>();
    private readonly IAccentService _accent = Substitute.For<IAccentService>();

    [Fact]
    public void Constructor_MapsCurrentThemeToIndex()
    {
        _theme.Theme.Returns(ElementTheme.Dark);
        Assert.Equal(2, CreateViewModel().ThemeIndex);

        _theme.Theme.Returns(ElementTheme.Light);
        Assert.Equal(1, CreateViewModel().ThemeIndex);

        _theme.Theme.Returns(ElementTheme.Default);
        Assert.Equal(0, CreateViewModel().ThemeIndex);
    }

    [Fact]
    public void ThemeIndexChange_AppliesMappedTheme()
    {
        // Starts at index 0 (theme defaults to ElementTheme.Default).
        var viewModel = CreateViewModel();

        viewModel.ThemeIndex = 2;
        viewModel.ThemeIndex = 1;
        viewModel.ThemeIndex = 0;

        _theme.Received(1).SetTheme(ElementTheme.Dark);
        _theme.Received(1).SetTheme(ElementTheme.Light);
        _theme.Received(1).SetTheme(ElementTheme.Default);
    }

    [Fact]
    public void Constructor_MapsCurrentAccentToIndex()
    {
        _accent.Accent.Returns(AccentSource.App);
        Assert.Equal(1, CreateViewModel().AccentIndex);

        _accent.Accent.Returns(AccentSource.Windows);
        Assert.Equal(0, CreateViewModel().AccentIndex);
    }

    [Fact]
    public void AccentIndexChange_AppliesMappedSource()
    {
        // Starts at index 0 (accent defaults to AccentSource.Windows).
        var viewModel = CreateViewModel();

        viewModel.AccentIndex = 1;
        viewModel.AccentIndex = 0;

        _accent.Received(1).SetAccent(AccentSource.App);
        _accent.Received(1).SetAccent(AccentSource.Windows);
    }

    [Fact]
    public void HasAccentChoice_FollowsTheService()
    {
        _accent.HasAppAccent.Returns(true);
        Assert.True(CreateViewModel().HasAccentChoice);

        _accent.HasAppAccent.Returns(false);
        Assert.False(CreateViewModel().HasAccentChoice);
    }

    private SettingsViewModel CreateViewModel() => new(_theme, _accent);
}
