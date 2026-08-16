using System.Threading;
using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

/// <summary>
/// A cross-cutting step wrapped around request handling (for example logging, validation or
/// transactions). Behaviors form a pipeline and must call <c>next</c> to continue.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Runs this step. Call <paramref name="next"/> to continue the pipeline.</summary>
    /// <param name="request">The request travelling the pipeline.</param>
    /// <param name="next">The rest of the pipeline, ending at the handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The response, from <paramref name="next"/> or from this behavior.</returns>
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
