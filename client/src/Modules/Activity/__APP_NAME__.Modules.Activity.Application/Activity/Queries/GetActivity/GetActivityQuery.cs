using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Activity.Application.Activity.Queries.GetActivity;

/// <summary>Asks for one page of the signed-in account's feed, newest first.</summary>
/// <remarks>
/// No account: whose feed it is comes from the token the transport already carries, so there is
/// nothing here that could ask for somebody else's.
/// </remarks>
public sealed record GetActivityQuery(ActivityFeedFilter Filter)
    : IRequest<Result<ActivityPageDto>>;
