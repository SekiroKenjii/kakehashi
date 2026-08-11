using System;
using Kakehashi.App.Hosting.Orchestration;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml;

namespace Kakehashi.App.Services.Platform {
  // Exposes the main window created during startup to modules via the UI contract.
  public sealed class MainWindowProvider : IMainWindowProvider {
    private readonly StartupContext _context;

    public MainWindowProvider(StartupContext context) {
      ArgumentNullException.ThrowIfNull(context);
      _context = context;
    }

    public Window? MainWindow => _context.MainWindow;
  }
}
