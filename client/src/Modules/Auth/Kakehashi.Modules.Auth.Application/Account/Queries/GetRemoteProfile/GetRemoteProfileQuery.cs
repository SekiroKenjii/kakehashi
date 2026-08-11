using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Queries.GetRemoteProfile {
  // Reads the user's profile from the authorization server.
  public sealed record GetRemoteProfileQuery : IRequest<Result<RemoteProfileDto>>;
}
