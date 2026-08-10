using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Activity.Application.Tests.Activity.Queries {
  public sealed class GetActivityQueryHandlerTests {
    private readonly IActivityGateway _activity = Substitute.For<IActivityGateway>();

    private GetActivityQueryHandler CreateHandler() {
      return new GetActivityQueryHandler(_activity);
    }

    private static ActivityEntryDto Entry(string kind) {
      return new ActivityEntryDto(kind, "laptop", "10.0.0.1", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_PassesTheRequestedTakeThrough() {
      _activity.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>([Entry("SignedIn")]));

      var result = await CreateHandler().Handle(new GetActivityQuery(7), CancellationToken.None);

      Assert.True(result.IsSuccess);
      await _activity.Received(1).ListAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsTheEntriesTheGatewayGave() {
      _activity.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success<IReadOnlyList<ActivityEntryDto>>(
              [Entry("SignedIn"), Entry("SignedOut")]));

      var result = await CreateHandler().Handle(new GetActivityQuery(), CancellationToken.None);

      Assert.True(result.IsSuccess);
      Assert.Equal(2, result.Value.Count);
      Assert.Equal("SignedIn", result.Value[0].Kind);
    }

    [Fact]
    public async Task Handle_PropagatesAFailureUnchanged() {
      // The handler must not translate: the gateway already decided which failure this is, and
      // NotSignedIn versus RequestFailed is the distinction the view model acts on.
      _activity.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
          .Returns(Result.Failure<IReadOnlyList<ActivityEntryDto>>(ActivityErrors.NotSignedIn));

      var result = await CreateHandler().Handle(new GetActivityQuery(), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(ActivityErrors.NotSignedIn, result.Error);
    }
  }
}
