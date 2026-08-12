using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kakehashi.UI.Contracts.Services.Platform;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Kakehashi.App.Services.Platform {
  // A picker created in a desktop app has no window of its own to be modal to: without
  // InitializeWithWindow it throws rather than appearing. That one call is most of the reason this
  // lives behind a port.
  public sealed class FileSaveService : IFileSaveService {
    private readonly IMainWindowProvider _windows;

    public FileSaveService(IMainWindowProvider windows) {
      ArgumentNullException.ThrowIfNull(windows);
      _windows = windows;
    }

    public async Task<string?> PickSaveLocationAsync(
        string suggestedName, string fileTypeLabel, string extension) {
      if (_windows.MainWindow is not { } window) {
        return null;
      }

      var picker = new FileSavePicker {
        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        SuggestedFileName = suggestedName,
      };
      picker.FileTypeChoices.Add(fileTypeLabel, new List<string> { extension });

      InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

      var file = await picker.PickSaveFileAsync();
      return file?.Path;
    }
  }
}
