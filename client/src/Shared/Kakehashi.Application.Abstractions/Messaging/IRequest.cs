namespace Kakehashi.Application.Abstractions.Messaging;

/// <summary>A request (command or query) handled by exactly one handler.</summary>
/// <typeparam name="TResponse">The type returned by the handler.</typeparam>
public interface IRequest<out TResponse>
{
}

/// <summary>A request that produces no value.</summary>
public interface IRequest : IRequest<Unit>
{
}
