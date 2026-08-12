using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Queries.GetActivity {
  // No account in the request: whose feed it is comes from the token the transport already carries,
  // so there is nothing here that could ask for somebody else's.
  public sealed record GetActivityQuery(ActivityFeedFilter Filter)
      : IRequest<Result<ActivityPageDto>>;
}
