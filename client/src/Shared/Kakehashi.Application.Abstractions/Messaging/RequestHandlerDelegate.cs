using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
}
