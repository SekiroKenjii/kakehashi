using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Activity.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Activity.Application.Activity.Commands.RecordClientEvent;

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
