using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Mediator.Tests {
  public sealed class CallRecorder {
    public List<string> Calls { get; } = new();
  }

  public sealed record Ping(string Text) : IRequest<string>;

  public sealed class PingHandler : IRequestHandler<Ping, string> {
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) {
      return Task.FromResult("pong:" + request.Text);
    }
  }

  public sealed class AppendBehavior : IPipelineBehavior<Ping, string> {
    public async Task<string> Handle(
        Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) {
      var response = await next();
      return response + "+wrapped";
    }
  }

  public sealed record Pinged(string Text) : INotification;

  public sealed class FirstPingedHandler : INotificationHandler<Pinged> {
    private readonly CallRecorder _recorder;

    public FirstPingedHandler(CallRecorder recorder) {
      _recorder = recorder;
    }

    public Task Handle(Pinged notification, CancellationToken cancellationToken) {
      _recorder.Calls.Add("first");
      return Task.CompletedTask;
    }
  }

  public sealed class SecondPingedHandler : INotificationHandler<Pinged> {
    private readonly CallRecorder _recorder;

    public SecondPingedHandler(CallRecorder recorder) {
      _recorder = recorder;
    }

    public Task Handle(Pinged notification, CancellationToken cancellationToken) {
      _recorder.Calls.Add("second");
      return Task.CompletedTask;
    }
  }

  public sealed record ThingHappened(int Id) : IDomainEvent;

  public sealed class ThingHappenedHandler : IDomainEventHandler<ThingHappened> {
    private readonly CallRecorder _recorder;

    public ThingHappenedHandler(CallRecorder recorder) {
      _recorder = recorder;
    }

    public Task Handle(ThingHappened domainEvent, CancellationToken cancellationToken) {
      _recorder.Calls.Add("thing:" + domainEvent.Id);
      return Task.CompletedTask;
    }
  }
}
