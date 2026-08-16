using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

/// <summary>Invokes the next step in the request pipeline (the next behavior or the handler).</summary>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
