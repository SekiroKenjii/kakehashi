using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity;
using Kakehashi.Modules.Activity.UI.ViewModels;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Activity.UI.Tests.ViewModels {
  /// <summary>
  /// Unit tests for <see cref="ActivityViewModel"/>: the wording it chooses for each kind, the
  /// three states the page renders, and the one failure that must clear what is on screen.
  /// </summary>
  public sealed class ActivityViewModelTests {
    private readonly ISender _sender = Substitute.For<ISender>();

    private ActivityViewModel CreateViewModel() {
      return new ActivityViewModel(_sender);
    }

    private void Returns(Result<IReadOnlyList<ActivityEntryDto>> result) {
      _sender.Send(Arg.Is<GetActivityQuery>(query => query != null), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(result));
    }

    private static ActivityEntryDto Entry(string kind, string device = "laptop") {
      return new ActivityEntryDto(kind, device, "10.0.0.1", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Load_FillsTheFeedNewestFirstAsGiven() {
      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>(
          [Entry("SignedIn"), Entry("PasswordChanged")]));
      var viewModel = CreateViewModel();

      await viewModel.LoadCommand.ExecuteAsync(null);

      Assert.Equal(2, viewModel.Feed.Count);
      Assert.Equal("Signed in", viewModel.Feed[0].Title);
      Assert.Equal("Password changed", viewModel.Feed[1].Title);
      Assert.False(viewModel.HasError);
      Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Load_ShowsTheDeviceAndAddressThatAnswerWasThatMe() {
      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([Entry("SignedIn", "MACHINE-A")]));
      var viewModel = CreateViewModel();

      await viewModel.LoadCommand.ExecuteAsync(null);

      Assert.Contains("MACHINE-A", viewModel.Feed[0].Details, StringComparison.Ordinal);
      Assert.Contains("10.0.0.1", viewModel.Feed[0].Details, StringComparison.Ordinal);
      Assert.True(viewModel.Feed[0].HasDetails);
    }

    [Fact]
    public async Task Load_ShowsAnUnknownKindRatherThanDroppingIt() {
      // Every module added to this boilerplate contributes kinds of its own, and a feed that
      // silently hides what it does not recognise is a feed you cannot trust to be complete.
      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([Entry("SomethingNewEntirely")]));
      var viewModel = CreateViewModel();

      await viewModel.LoadCommand.ExecuteAsync(null);

      Assert.Single(viewModel.Feed);
      Assert.Equal("SomethingNewEntirely", viewModel.Feed[0].Title);
    }

    [Fact]
    public async Task Load_WithNothingToShow_IsEmptyRatherThanFailed() {
      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([]));
      var viewModel = CreateViewModel();

      await viewModel.LoadCommand.ExecuteAsync(null);

      Assert.True(viewModel.IsEmpty);
      Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Load_WhenTheNetworkFails_KeepsTheRowsItAlreadyHas() {
      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([Entry("SignedIn")]));
      var viewModel = CreateViewModel();
      await viewModel.LoadCommand.ExecuteAsync(null);

      Returns(Result.Failure<IReadOnlyList<ActivityEntryDto>>(ActivityErrors.RequestFailed));
      await viewModel.LoadCommand.ExecuteAsync(null);

      // The rows are still true; throwing them away would make a flaky connection look like an
      // empty history.
      Assert.Single(viewModel.Feed);
      Assert.True(viewModel.HasError);
      Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Load_WhenTheSessionIsGone_ClearsWhatIsOnScreen() {
      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([Entry("SignedIn")]));
      var viewModel = CreateViewModel();
      await viewModel.LoadCommand.ExecuteAsync(null);

      Returns(Result.Failure<IReadOnlyList<ActivityEntryDto>>(ActivityErrors.NotSignedIn));
      await viewModel.LoadCommand.ExecuteAsync(null);

      // A page left open across a sign-out on a shared machine must not keep showing the previous
      // account's devices and addresses to whoever signs in next.
      Assert.Empty(viewModel.Feed);
      Assert.Equal(ActivityErrors.NotSignedIn.Message, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_ClearsAPreviousErrorOnSuccess() {
      Returns(Result.Failure<IReadOnlyList<ActivityEntryDto>>(ActivityErrors.RequestFailed));
      var viewModel = CreateViewModel();
      await viewModel.LoadCommand.ExecuteAsync(null);
      Assert.True(viewModel.HasError);

      Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([Entry("SignedIn")]));
      await viewModel.LoadCommand.ExecuteAsync(null);

      Assert.False(viewModel.HasError);
      Assert.Single(viewModel.Feed);
    }
  }
}
