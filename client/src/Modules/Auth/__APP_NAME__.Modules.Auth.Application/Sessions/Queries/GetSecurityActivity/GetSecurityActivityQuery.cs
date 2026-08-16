using System.Collections.Generic;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity;

/// <summary>Lists the most recent entries of the user's security activity feed.</summary>
public sealed record GetSecurityActivityQuery(int Take = 20)
    : IRequest<Result<IReadOnlyList<SecurityEventDto>>>;
