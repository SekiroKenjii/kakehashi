using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Activity.Application.Tests.Activity.Commands.RecordClientEvent;

/// <summary>
/// Unit tests for <see cref="RecordClientEventCommandHandler"/>: the module's only write, and a
/// pass-through like its read counterpart.
/// </summary>
public sealed class RecordClientEventCommandHandlerTests
{
    private readonly IActivityGateway _activity = Substitute.For<IActivityGateway>();

    private RecordClientEventCommandHandler CreateHandler()
    {
        return new RecordClientEventCommandHandler(_activity);
    }

    [Theory]
    [InlineData(ClientActivityKind.AppUpdated)]
    [InlineData(ClientActivityKind.ThemeChanged)]
    public async Task Handle_ReportsTheKindItWasGiven(ClientActivityKind kind)
    {
        _activity.RecordAsync(kind, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var result = await CreateHandler()
            .Handle(new RecordClientEventCommand(kind), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _activity.Received(1).RecordAsync(kind, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PropagatesAFailureUnchanged()
    {
        _activity.RecordAsync(Arg.Any<ClientActivityKind>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(ActivityErrors.ReportRefused)));

        var result = await CreateHandler().Handle(
            new RecordClientEventCommand(ClientActivityKind.AppUpdated), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActivityErrors.ReportRefused, result.Error);
    }
}
