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

namespace Kakehashi.Modules.Activity.Application.Tests.Activity.Queries.GetActivity {
  // The point of these is that the handler stays a pass-through: everything the reader chose has to
  // reach the gateway unaltered.
  public sealed class GetActivityQueryHandlerTests {
    private readonly IActivityGateway _activity = Substitute.For<IActivityGateway>();

    private GetActivityQueryHandler CreateHandler() {
      return new GetActivityQueryHandler(_activity);
    }

    private static ActivityPageDto Page(params ActivityEntryDto[] entries) {
      return new ActivityPageDto(
          entries, NextPageToken: string.Empty, Total: entries.Length,
          Counts: new Dictionary<string, int>(), KindCounts: new Dictionary<string, int>(),
          RetentionDays: 90);
    }

    private static ActivityEntryDto Entry(string kind) {
      return new ActivityEntryDto(
          "id-1", kind, ActivityCategories.SignIn, "session-1", "laptop", "Windows", "10.0.0.1",
          DateTimeOffset.UtcNow);
    }

    // A range or a search dropped here would produce a page that looked plausible and answered a
    // different question than the one asked.
    [Fact]
    public async Task Handle_PassesTheWholeFilterThrough() {
      var filter = new ActivityFeedFilter {
        From = DateTimeOffset.UtcNow.AddDays(-7),
        To = DateTimeOffset.UtcNow,
        Category = ActivityCategories.Security,
        Search = "203.0.113",
        PageToken = "a-token",
        PageSize = 25,
      };
      _activity.ListAsync(filter, Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Success(Page())));

      await CreateHandler().Handle(new GetActivityQuery(filter), CancellationToken.None);

      await _activity.Received(1).ListAsync(filter, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsThePageTheGatewayGave() {
      var page = Page(Entry("SignedIn"), Entry("SignedOut"));
      _activity.ListAsync(Arg.Any<ActivityFeedFilter>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Success(page)));

      var result = await CreateHandler()
          .Handle(new GetActivityQuery(ActivityFeedFilter.Default), CancellationToken.None);

      Assert.True(result.IsSuccess);
      Assert.Same(page, result.Value);
    }

    [Fact]
    public async Task Handle_PropagatesAFailureUnchanged() {
      _activity.ListAsync(Arg.Any<ActivityFeedFilter>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Failure<ActivityPageDto>(ActivityErrors.NotSignedIn)));

      var result = await CreateHandler()
          .Handle(new GetActivityQuery(ActivityFeedFilter.Default), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(ActivityErrors.NotSignedIn, result.Error);
    }
  }
}
