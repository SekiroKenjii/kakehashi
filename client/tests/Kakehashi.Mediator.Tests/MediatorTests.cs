using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kakehashi.Mediator.Tests;

public sealed class MediatorTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CallRecorder>();
        services.AddMediator(typeof(MediatorTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_RoutesRequestToItsHandler()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new Ping("hi"));

        Assert.Equal("pong:hi", response);
    }

    [Fact]
    public async Task Send_RunsPipelineBehaviorsAroundTheHandler()
    {
        using var provider = BuildProvider(services =>
            services.AddTransient<IPipelineBehavior<Ping, string>, AppendBehavior>());
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.Send(new Ping("hi"));

        Assert.Equal("pong:hi+wrapped", response);
    }

    [Fact]
    public async Task Publish_InvokesEveryNotificationHandler()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var recorder = provider.GetRequiredService<CallRecorder>();

        await mediator.Publish(new Pinged("hi"));

        Assert.Contains("first", recorder.Calls);
        Assert.Contains("second", recorder.Calls);
    }

    [Fact]
    public async Task DispatchAsync_InvokesHandlerForEachDomainEvent()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        var recorder = provider.GetRequiredService<CallRecorder>();
        var events = new IDomainEvent[] { new ThingHappened(1), new ThingHappened(2) };

        await dispatcher.DispatchAsync(events, CancellationToken.None);

        Assert.Equal(new List<string> { "thing:1", "thing:2" }, recorder.Calls);
    }
}
