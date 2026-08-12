using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.UI.Contracts.Services.Platform {
  // In-app only. OS-level toasts are still a seam here because they require a packaged app.
  public interface INotificationService : IUiContractService, ISingletonDependency {
    // Call once, when the shell's notification bar loads.
    void Initialize(InfoBar infoBar);

    void Show(
        string message,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        string? title = null);
  }
}
