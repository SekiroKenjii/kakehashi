using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity;

/// <summary>Lists the most recent entries of the user's security activity feed.</summary>
public sealed record GetSecurityActivityQuery(int Take = 20)
    : IRequest<Result<IReadOnlyList<SecurityEventDto>>>;
