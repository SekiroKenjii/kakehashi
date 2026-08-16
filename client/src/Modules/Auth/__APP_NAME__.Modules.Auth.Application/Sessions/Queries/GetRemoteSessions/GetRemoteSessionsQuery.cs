using System.Collections.Generic;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions;

/// <summary>Lists the user's active sessions on the authorization server.</summary>
public sealed record GetRemoteSessionsQuery : IRequest<Result<IReadOnlyList<RemoteSessionDto>>>;
