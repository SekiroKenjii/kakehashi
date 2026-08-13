using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Activity.Application.Activity;
using Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Kakehashi.Modules.Activity.UI {
  /// <summary>
  /// Forwards the two app-level facts the server cannot observe for itself into the account's feed.
  /// </summary>
  /// <remarks>
  /// The host announces app updates and theme changes over the messenger — it does not reference
  /// feature modules — and this listens. Only those two of the host's four kinds are forwarded:
  /// the server writes sign-ins and sign-outs from facts it observes itself, and forwarding them
  /// would double every sign-in in the feed.
  /// </remarks>
  public sealed partial class ActivityReporter : IAwakeOnStartup {
    private readonly ISender _sender;
    private readonly ILogger<ActivityReporter> _logger;

    public ActivityReporter(ISender sender, ILogger<ActivityReporter> logger) {
      ArgumentNullException.ThrowIfNull(sender);
      ArgumentNullException.ThrowIfNull(logger);
      _sender = sender;
      _logger = logger;
    }

    public string Name => "Activity reporter";

    public void Initialize(IServiceProvider serviceProvider) {
      ArgumentNullException.ThrowIfNull(serviceProvider);

      // App-lifetime singleton, so the registration is deliberately never undone. The static
      // recipient keeps this from closing over the instance, which is what would otherwise let the
      // messenger's weak reference be the only thing keeping it alive.
      WeakReferenceMessenger.Default.Register<ActivityReporter, AppActivityRecordedMessage>(
          this, static (reporter, message) => reporter.Forward(message.Kind));
    }

    private void Forward(AppActivityKind kind) {
      if (Reportable(kind) is not { } reportable) {
        return;
      }

      // Unawaited: a failure is a log line, not anything a person sees. An exception must not
      // escape into the messenger's dispatch, which would take down whichever thread announced
      // the fact.
      _ = ReportAsync(reportable);
    }

    private async Task ReportAsync(ClientActivityKind kind) {
      try {
        var result = await _sender
            .Send(new RecordClientEventCommand(kind), CancellationToken.None)
            .ConfigureAwait(false);

        if (result.IsFailure) {
          LogNotReported(kind, result.Error.Message);
        }
      } catch (Exception exception) {
        // Deliberately broad. This is a background report of something that already happened; there
        // is no caller to hand an exception to, and letting one escape an unawaited task would end
        // the process on an unobserved-exception policy.
        LogNotReportedAtAll(kind, exception);
      }
    }

    /// <summary>
    /// Which announced facts this module reports, and which it leaves alone.
    /// </summary>
    /// <remarks>
    /// Sign-ins and sign-outs are absent on purpose: the server records those from its own events, so
    /// reporting them here would put two rows in the feed for one thing that happened.
    /// </remarks>
    private static ClientActivityKind? Reportable(AppActivityKind kind) {
      return kind switch {
        AppActivityKind.AppUpdated => ClientActivityKind.AppUpdated,
        AppActivityKind.ThemeChanged => ClientActivityKind.ThemeChanged,
        _ => null,
      };
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Activity fact {Kind} was not reported: {Reason}")]
    private partial void LogNotReported(ClientActivityKind kind, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reporting activity fact {Kind} threw.")]
    private partial void LogNotReportedAtAll(ClientActivityKind kind, Exception exception);
  }
}
