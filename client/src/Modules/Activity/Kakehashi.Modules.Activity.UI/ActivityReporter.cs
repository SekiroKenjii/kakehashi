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
  // Forwards the two app-level facts the server cannot observe for itself into the account's feed.
  //
  // The host notices when this installation has been updated and when the theme changes, and
  // announces both. It cannot hand them to this module directly — the host does not reference a
  // feature module — so it announces and this listens.
  //
  // It lives in the module rather than in the host because what belongs in the feed is the activity
  // module's business. The host's own local log keeps recording all four kinds it knows about for its
  // Home page; only two of them are this module's to report, because the server already writes the
  // other two from facts it saw itself. Forwarding those would double every sign-in.
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

      // Nobody asked for this and nobody is waiting on it, so it runs unawaited and a failure is a
      // log line rather than anything a person sees. What must not happen is an exception escaping
      // into the messenger's dispatch, which would take down whichever thread announced the fact.
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

    // Which announced facts this module reports, and which it leaves alone.
    //
    // Sign-ins and sign-outs are absent on purpose: the server records those from its own events, so
    // reporting them here would put two rows in the feed for one thing that happened.
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
