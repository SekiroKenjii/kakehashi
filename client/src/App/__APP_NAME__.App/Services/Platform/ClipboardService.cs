using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Windows.ApplicationModel.DataTransfer;

namespace __ROOT_NAMESPACE__.App.Services.Platform;

/// <summary>Puts text on the Windows clipboard.</summary>
public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);

        // Without this the package is held by reference until something flushes it to the OS, so a
        // copy outlives this process only if the app stays open.
        Clipboard.Flush();
    }
}
