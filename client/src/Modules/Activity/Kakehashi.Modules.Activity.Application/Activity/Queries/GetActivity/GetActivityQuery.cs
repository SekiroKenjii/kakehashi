using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity {
  /// <summary>Lists the signed-in account's recent activity, newest first.</summary>
  /// <param name="Take">How many entries to ask for. The server clamps anything unreasonable.</param>
  public sealed record GetActivityQuery(int Take = 50)
      : IRequest<Result<IReadOnlyList<ActivityEntryDto>>>;
}
