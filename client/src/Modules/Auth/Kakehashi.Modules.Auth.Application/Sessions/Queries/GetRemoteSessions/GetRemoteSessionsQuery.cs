using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions {
  public sealed record GetRemoteSessionsQuery : IRequest<Result<IReadOnlyList<RemoteSessionDto>>>;
}
