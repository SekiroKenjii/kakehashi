using Microsoft.UI.Xaml;

namespace Kakehashi.UI.Contracts.Services.Platform {
  public interface IMainWindowProvider : IUiContractService {
    // Null until the shell has been created, which includes anything running during startup.
    Window? MainWindow { get; }
  }
}
