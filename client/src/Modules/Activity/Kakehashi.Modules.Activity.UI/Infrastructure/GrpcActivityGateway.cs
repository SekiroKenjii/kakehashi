using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Logging;
using ActivityV1 = Kakehashi.Activity.V1;

namespace Kakehashi.Modules.Activity.UI.Infrastructure {
  // The alias is ActivityV1 rather than Activity because inside a Kakehashi.* namespace the
  // identifier Activity binds to the enclosing Kakehashi.Modules.Activity namespace before a using
  // alias is ever considered.
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

    public async Task<Result<ActivityPageDto>> ListAsync(
        ActivityFeedFilter filter, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(filter);

      var request = new ActivityV1.ListActivityRequest {
        PageSize = filter.PageSize,
        PageToken = filter.PageToken,
        Category = filter.Category,
        Query = filter.Search,
      };

      // Left unset rather than sent as a zero timestamp: the server reads an absent bound as
      // unbounded, and the epoch is a real instant that would look like a deliberate bound.
      if (filter.From is { } from) {
        request.From = Timestamp.FromDateTimeOffset(from);
      }
      if (filter.To is { } to) {
        request.To = Timestamp.FromDateTimeOffset(to);
      }

      try {
        var reply = await _client
            .ListActivityAsync(request, cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        var entries = new List<ActivityEntryDto>(reply.Entries.Count);
        foreach (var entry in reply.Entries) {
          entries.Add(ToDto(entry));
        }

        var counts = new Dictionary<string, int>(reply.Counts.Count, StringComparer.Ordinal);
        foreach (var count in reply.Counts) {
          counts[count.Category] = count.Count;
        }

        var kindCounts = new Dictionary<string, int>(
            reply.KindCounts.Count, StringComparer.Ordinal);
        foreach (var count in reply.KindCounts) {
          kindCounts[count.Kind] = count.Count;
        }

        return Result.Success(new ActivityPageDto(
            entries, reply.NextPageToken, reply.TotalCount, counts, kindCounts,
            reply.RetentionDays));
      } catch (RpcException exception) {
        // InvalidArgument here is an unreadable page token: this client and the server disagree
        // about where the reader is. Its own failure so the view model can start the list again
        // rather than sit under a "load more" button that will never work.
        if (exception.StatusCode == StatusCode.InvalidArgument) {
          LogFailed(exception.StatusCode, exception);
          return Result.Failure<ActivityPageDto>(ActivityErrors.PageLost);
        }
        return Result.Failure<ActivityPageDto>(Translate(exception));
      }
    }

    public async Task<Result> RecordAsync(
        ClientActivityKind kind, CancellationToken cancellationToken) {
      try {
        await _client
            .RecordClientEventAsync(
                new ActivityV1.RecordClientEventRequest { Kind = WireName(kind) },
                cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);
        return Result.Success();
      } catch (RpcException exception) {
        // Mapped here rather than in Translate because InvalidArgument means something different on
        // each call: on a list it is an unreadable page token, and here it is the server refusing a
        // kind, which can only happen if this client is newer than the server.
        if (exception.StatusCode == StatusCode.InvalidArgument) {
          LogFailed(exception.StatusCode, exception);
          return Result.Failure(ActivityErrors.ReportRefused);
        }
        return Result.Failure(Translate(exception));
      }
    }

    // No default arm, on purpose: adding a value to ClientActivityKind without deciding what to
    // call it on the wire should not compile. A default that sent the enum's own name would send a
    // string the server has never heard of, turning a missed edit into a runtime refusal.
    private static string WireName(ClientActivityKind kind) {
      return kind switch {
        ClientActivityKind.AppUpdated => "AppUpdated",
        ClientActivityKind.ThemeChanged => "ThemeChanged",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
      };
    }

    private static ActivityEntryDto ToDto(ActivityV1.Entry entry) {
      return new ActivityEntryDto(
          entry.Id,
          entry.Kind,
          entry.Category,
          entry.SessionId,
          entry.Device,
          entry.Platform,
          entry.IpAddress,
          entry.OccurredAt.ToDateTimeOffset());
    }

    private Error Translate(RpcException exception) {
      // Kept distinct rather than collapsed into the rest, because it is the one failure whose
      // correct handling is to stop showing what is on screen: a page left open across a sign-out
      // would otherwise keep the previous account's devices and addresses visible.
      if (exception.StatusCode == StatusCode.Unauthenticated) {
        return ActivityErrors.NotSignedIn;
      }

      // The server gates whole modules, so this means an administrator has not assigned this
      // account the Activity module — the one failure here the user can act on.
      if (exception.StatusCode == StatusCode.PermissionDenied) {
        return ActivityErrors.NotAssigned;
      }

      // Everything else is the network, the server, or a bug — none of it actionable beyond trying
      // again, so the detail goes to the log rather than the screen.
      LogFailed(exception.StatusCode, exception);
      return ActivityErrors.RequestFailed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Activity request failed with {Status}.")]
    private partial void LogFailed(StatusCode status, Exception exception);
  }
}
