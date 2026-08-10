using Kakehashi.UI.Contracts.Services.Platform;
using Windows.ApplicationModel.DataTransfer;

namespace Kakehashi.App.Services.Platform {
  /// <summary>Puts text on the Windows clipboard.</summary>
  public sealed class ClipboardService : IClipboardService {
    public void SetText(string text) {
      var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
      package.SetText(text);
      Clipboard.SetContent(package);

      // Without this the clipboard's contents live only as long as this process: the package is
      // held by reference until something flushes it to the OS. Copying an email and then closing
      // the app would otherwise paste nothing.
      Clipboard.Flush();
    }
  }
}
