using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity {
  // Lists the most recent entries of the user's security activity feed.
  public sealed record GetSecurityActivityQuery(int Take = 20)
      : IRequest<Result<IReadOnlyList<SecurityEventDto>>>;
}
