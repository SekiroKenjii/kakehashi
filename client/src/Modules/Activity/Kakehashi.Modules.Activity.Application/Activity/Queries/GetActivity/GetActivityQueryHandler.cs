using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity {
  /// <summary>Fetches the feed through the gateway.</summary>
  public sealed class GetActivityQueryHandler
      : IRequestHandler<GetActivityQuery, Result<IReadOnlyList<ActivityEntryDto>>> {
    private readonly IActivityGateway _activity;

    public GetActivityQueryHandler(IActivityGateway activity) {
      ArgumentNullException.ThrowIfNull(activity);
      _activity = activity;
    }

    // A pass-through, and it stays one until this query grows a reason not to be — grouping by day,
    // merging in something the client knows and the server does not. The handler is where that
    // would live, which is why the view model talks to it rather than to the gateway.
    public Task<Result<IReadOnlyList<ActivityEntryDto>>> Handle(
        GetActivityQuery request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);
      return _activity.ListAsync(request.Take, cancellationToken);
    }
  }
}
