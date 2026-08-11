using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Activity.UI.Tests {
  /// <summary>
  /// Unit tests for <see cref="ActivityReporter"/>: which announced facts reach the server, and — the
  /// part worth a test — which do not.
  /// </summary>
  /// <remarks>
  /// The reporter registers with the static <c>WeakReferenceMessenger</c>, so the instance is held in
  /// a field for the life of each test (a weakly-held recipient can be collected mid-test) and
  /// unregistered on teardown to leave the shared bus clean for the next one.
  /// </remarks>
  public sealed class ActivityReporterTests : IDisposable {
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ActivityReporter _reporter;

    public ActivityReporterTests() {
      _sender.Send(
              Arg.Is<RecordClientEventCommand>(command => command != null),
              Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Success()));

      _reporter = new ActivityReporter(_sender, Substitute.For<ILogger<ActivityReporter>>());
      _reporter.Initialize(Substitute.For<IServiceProvider>());
    }

    public void Dispose() {
      WeakReferenceMessenger.Default.UnregisterAll(_reporter);
    }

    [Theory]
    [InlineData(AppActivityKind.AppUpdated, ClientActivityKind.AppUpdated)]
    [InlineData(AppActivityKind.ThemeChanged, ClientActivityKind.ThemeChanged)]
    public void AFactTheServerCannotSeeForItselfIsReported(
        AppActivityKind announced, ClientActivityKind expected) {
      WeakReferenceMessenger.Default.Send(new AppActivityRecordedMessage(announced));

      _sender.Received(1).Send(
          Arg.Is<RecordClientEventCommand>(command => command != null && command.Kind == expected),
          Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The server writes sign-ins and sign-outs from its own events, so forwarding the host's copy
    /// would put two rows in the feed for one thing that happened.
    /// </summary>
    [Theory]
    [InlineData(AppActivityKind.SignedIn)]
    [InlineData(AppActivityKind.SignedOut)]
    public void AFactTheServerAlreadyRecordsIsNotReportedAgain(AppActivityKind announced) {
      WeakReferenceMessenger.Default.Send(new AppActivityRecordedMessage(announced));

      _sender.DidNotReceive().Send(
          Arg.Is<RecordClientEventCommand>(command => command != null),
          Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refusal is a log line, not an exception: nobody asked for the report and nobody is waiting
    /// on it, and an exception escaping an unawaited task would end the process.
    /// </summary>
    [Fact]
    public void AFailedReportIsSwallowed() {
      _sender.Send(
              Arg.Is<RecordClientEventCommand>(command => command != null),
              Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(Result.Failure(ActivityErrors.RequestFailed)));

      WeakReferenceMessenger.Default.Send(
          new AppActivityRecordedMessage(AppActivityKind.ThemeChanged));

      _sender.Received(1).Send(
          Arg.Is<RecordClientEventCommand>(command => command != null),
          Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AThrowingSenderDoesNotReachTheAnnouncer() {
      _sender.Send(
              Arg.Is<RecordClientEventCommand>(command => command != null),
              Arg.Any<CancellationToken>())
          .Returns<Task<Result>>(_ => throw new InvalidOperationException("the mediator threw"));

      // The announcer is whichever thread noticed the fact — the UI thread, at startup. An exception
      // that got back to it would take down the app over a theme change.
      WeakReferenceMessenger.Default.Send(
          new AppActivityRecordedMessage(AppActivityKind.ThemeChanged));
    }
  }
}
