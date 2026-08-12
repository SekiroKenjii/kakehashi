using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Queries.GetRemoteProfile {
  public sealed record GetRemoteProfileQuery : IRequest<Result<RemoteProfileDto>>;
}
