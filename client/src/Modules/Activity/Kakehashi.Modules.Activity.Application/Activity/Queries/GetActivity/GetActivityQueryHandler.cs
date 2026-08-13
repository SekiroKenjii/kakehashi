using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity {
  public sealed class GetActivityQueryHandler
      : IRequestHandler<GetActivityQuery, Result<ActivityPageDto>> {
    private readonly IActivityGateway _activity;

    public GetActivityQueryHandler(IActivityGateway activity) {
      ArgumentNullException.ThrowIfNull(activity);
      _activity = activity;
    }

    // Day grouping and burst grouping are presentation — they depend on the reader's time zone and
    // on how rows are drawn — so they belong to the view model. What belongs here is anything that
    // changes *which* entries the page is about.
    public Task<Result<ActivityPageDto>> Handle(
        GetActivityQuery request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);
      return _activity.ListAsync(request.Filter, cancellationToken);
    }
  }
}
