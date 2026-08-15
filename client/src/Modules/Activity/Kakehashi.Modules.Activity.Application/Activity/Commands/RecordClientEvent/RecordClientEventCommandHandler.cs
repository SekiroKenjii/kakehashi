using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent;

public sealed class RecordClientEventCommandHandler
    : IRequestHandler<RecordClientEventCommand, Result>
{
    private readonly IActivityGateway _activity;

    public RecordClientEventCommandHandler(IActivityGateway activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activity = activity;
    }

    public Task<Result> Handle(
        RecordClientEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _activity.RecordAsync(request.Kind, cancellationToken);
    }
}
