using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Logging;
using ActivityV1 = Kakehashi.Activity.V1;

namespace Kakehashi.Modules.Activity.UI.Infrastructure {
  /// <summary>
  /// The gRPC adapter behind <see cref="IActivityGateway"/>. It is the only class in the module
  /// that knows the wire exists: it maps the generated messages onto the application's DTOs and
  /// turns transport failures into <see cref="Result"/> failures.
  /// </summary>
  /// <remarks>
  /// The alias is <c>ActivityV1</c> rather than <c>Activity</c> because inside a
  /// <c>Kakehashi.*</c> namespace the identifier <c>Activity</c> binds to the enclosing
  /// <c>Kakehashi.Modules.Activity</c> namespace before a using alias is ever considered.
  /// </remarks>
  public sealed partial class GrpcActivityGateway : IActivityGateway {
    private readonly ActivityV1.ActivityService.ActivityServiceClient _client;
    private readonly ILogger<GrpcActivityGateway> _logger;

    public GrpcActivityGateway(
        ActivityV1.ActivityService.ActivityServiceClient client,
        ILogger<GrpcActivityGateway> logger) {
      ArgumentNullException.ThrowIfNull(client);
      ArgumentNullException.ThrowIfNull(logger);
      _client = client;
      _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ActivityEntryDto>>> ListAsync(
        int take, CancellationToken cancellationToken) {
      try {
        var reply = await _client
            .ListActivityAsync(
                new ActivityV1.ListActivityRequest { PageSize = take },
                cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        var entries = new List<ActivityEntryDto>(reply.Entries.Count);
        foreach (var entry in reply.Entries) {
          entries.Add(ToDto(entry));
        }
        return Result.Success<IReadOnlyList<ActivityEntryDto>>(entries);
      } catch (RpcException exception) {
        return Result.Failure<IReadOnlyList<ActivityEntryDto>>(Translate(exception));
      }
    }

    private static ActivityEntryDto ToDto(ActivityV1.Entry entry) {
      return new ActivityEntryDto(
          entry.Kind, entry.Device, entry.IpAddress, entry.OccurredAt.ToDateTimeOffset());
    }

    private Error Translate(RpcException exception) {
      // Unauthenticated is kept distinct from everything else rather than collapsed into it,
      // because it is the one failure whose correct handling is to stop showing what is on screen.
      // A page left open across a sign-out keeps polling, and without this it would keep the
      // previous account's devices and addresses visible indefinitely.
      if (exception.StatusCode == StatusCode.Unauthenticated) {
        return ActivityErrors.NotSignedIn;
      }

      // The server gates whole modules, so this means an administrator has not assigned this
      // account the Activity module — a thing the user can act on, unlike everything below.
      if (exception.StatusCode == StatusCode.PermissionDenied) {
        return ActivityErrors.NotAssigned;
      }

      // Everything else is the network, the server, or a bug — none of which the user can act on
      // beyond trying again. The detail goes to the log, not the screen.
      LogFailed(exception.StatusCode, exception);
      return ActivityErrors.RequestFailed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Activity list failed with {Status}.")]
    private partial void LogFailed(StatusCode status, Exception exception);
  }
}
