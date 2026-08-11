using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions {
  // Lists the user's active sessions on the authorization server.
  public sealed record GetRemoteSessionsQuery : IRequest<Result<IReadOnlyList<RemoteSessionDto>>>;
}
