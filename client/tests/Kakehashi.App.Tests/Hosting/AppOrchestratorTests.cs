using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Hosting.Orchestration;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.Hosting {
  // Run with an empty awake-service list so the loop that touches the app's static service provider
  // is never entered.
  public sealed class AppOrchestratorTests {
    private static readonly IEnumerable<IAwakeOnStartup> _noAwakeServices = [];

    private readonly ILogger<AppOrchestrator> _logger = Substitute.For<ILogger<AppOrchestrator>>();

    [Fact]
    public async Task StartAsync_RunsOrchestratorsInAscendingOrder() {
      var executed = new List<string>();
      var orchestrator = new AppOrchestrator(
          [RecordingOrchestrator(3, "C", executed),
           RecordingOrchestrator(1, "A", executed),
           RecordingOrchestrator(2, "B", executed)],
          new StartupContext(),
          _logger);

      await orchestrator.StartAsync(_noAwakeServices);

      Assert.Equal(["A", "B", "C"], executed);
    }

    [Fact]
    public async Task StartAsync_WhenCancelled_ThrowsAndSkipsOrchestrators() {
      var executed = new List<string>();
      var orchestrator = new AppOrchestrator(
          [RecordingOrchestrator(1, "A", executed)], new StartupContext(), _logger);
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
          () => orchestrator.StartAsync(_noAwakeServices, cts.Token));
      Assert.Empty(executed);
    }

    private static IStartupOrchestrator RecordingOrchestrator(
        int order, string name, List<string> sink) {
      var orchestrator = Substitute.For<IStartupOrchestrator>();
      orchestrator.Order.Returns(order);
      orchestrator.Name.Returns(name);
      orchestrator.Description.Returns(name);
      orchestrator.ExecuteAsync(Arg.Any<CancellationToken>())
          .Returns(_ => {
            sink.Add(name);
            return Task.CompletedTask;
          });
      return orchestrator;
    }
  }
}
