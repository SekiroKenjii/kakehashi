using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity {
  // Fetches a page of the feed through the gateway.
  public sealed class GetActivityQueryHandler
      : IRequestHandler<GetActivityQuery, Result<ActivityPageDto>> {
    private readonly IActivityGateway _activity;

    public GetActivityQueryHandler(IActivityGateway activity) {
      ArgumentNullException.ThrowIfNull(activity);
      _activity = activity;
    }

    // Still a pass-through. Day grouping and burst grouping, the two things that could have landed
    // here, are presentation: they depend on the reader's time zone and on how the rows are drawn, so
    // they belong to the view model. What would land here is anything that changed *which* entries
    // the page is about.
    public Task<Result<ActivityPageDto>> Handle(
        GetActivityQuery request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);
      return _activity.ListAsync(request.Filter, cancellationToken);
    }
  }
}
