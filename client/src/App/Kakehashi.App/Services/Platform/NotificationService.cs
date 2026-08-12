using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.Services.Platform {
  // A seam is left for OS-level toast notifications, which require a packaged app and
  // AppNotificationManager.
  public sealed class NotificationService : INotificationService {
    private InfoBar? _infoBar;

    public void Initialize(InfoBar infoBar) {
      _infoBar = infoBar;
    }

    public void Show(
        string message,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        string? title = null) {
      if (_infoBar is null) {
        return;
      }

      _infoBar.Title = title ?? string.Empty;
      _infoBar.Message = message;
      _infoBar.Severity = severity;
      _infoBar.IsOpen = true;
    }
  }
}
