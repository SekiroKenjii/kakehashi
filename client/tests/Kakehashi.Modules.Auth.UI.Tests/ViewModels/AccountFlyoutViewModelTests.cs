using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions;
using Kakehashi.Modules.Auth.UI.ViewModels;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Xaml;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Auth.UI.Tests.ViewModels {
  public sealed class AccountFlyoutViewModelTests {
    private static readonly DateTimeOffset _now =
        new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IThemeService _theme = Substitute.For<IThemeService>();
    private readonly IConfiguration _configuration =
        new ConfigurationBuilder().Build();
    private readonly IClock _clock = Substitute.For<IClock>();
    private SessionDto _session = SignedOut();

    public AccountFlyoutViewModelTests() {
      _clock.UtcNow.Returns(_now);
      _sender.Send(Arg.Is<GetCurrentSessionQuery>(request => request != null)).Returns(_ => Task.FromResult(_session));
      _sender.Send(Arg.Is<SignOutCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
    }

    [Fact]
    public async Task Load_WhenSignedIn_PopulatesStateAndAgo() {
      _theme.Theme.Returns(ElementTheme.Dark);
      _session = new SessionDto(
          true, "Vo Thuong", "vo@example.com", null, _now.AddHours(-2).AddMinutes(-5), []);
      var viewModel = CreateViewModel();

      await viewModel.LoadCommand.ExecuteAsync(parameter: null);

      Assert.True(viewModel.IsAuthenticated);
      Assert.Equal("Vo Thuong", viewModel.DisplayName);
      Assert.Equal("Vo Thuong", viewModel.AvatarName);
      Assert.Equal("vo@example.com", viewModel.Email);
      Assert.True(viewModel.HasEmail);
      Assert.Equal("Online", viewModel.StatusText);
      Assert.Equal(2, viewModel.ThemeIndex);
      Assert.Equal("2h 5m ago", viewModel.SignedInText);
    }

    [Fact]
    public async Task Load_WhenSignedOut_ShowsOfflinePlaceholders() {
      var viewModel = CreateViewModel();

      await viewModel.LoadCommand.ExecuteAsync(parameter: null);

      Assert.False(viewModel.IsAuthenticated);
      Assert.Equal("Not signed in", viewModel.DisplayName);
      Assert.Null(viewModel.AvatarName);
      Assert.False(viewModel.HasEmail);
      Assert.Equal("Offline", viewModel.StatusText);
      Assert.Equal("—", viewModel.SignedInText);
    }

    [Fact]
    public async Task SignOut_WhenAuthenticated_DispatchesSignOut() {
      _session = new SessionDto(true, "Vo", "vo@example.com", null, _now, []);
      var viewModel = CreateViewModel();
      await viewModel.LoadCommand.ExecuteAsync(parameter: null);

      await viewModel.SignOutCommand.ExecuteAsync(parameter: null);

      await _sender.Received(1).Send(Arg.Any<SignOutCommand>());
    }

    [Fact]
    public async Task SignOut_WhenNotAuthenticated_DoesNothing() {
      var viewModel = CreateViewModel();

      await viewModel.SignOutCommand.ExecuteAsync(parameter: null);

      await _sender.DidNotReceive().Send(Arg.Any<SignOutCommand>());
    }

    private AccountFlyoutViewModel CreateViewModel() {
      return new AccountFlyoutViewModel(_sender, _navigation, _theme, _clock, _configuration);
    }

    private static SessionDto SignedOut() {
      return new SessionDto(false, null, null, null, null, []);
    }
  }
}
