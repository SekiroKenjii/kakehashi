using System;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace __ROOT_NAMESPACE__.App.Services.Platform;

/// <summary>Shows the Windows open dialog.</summary>
/// <remarks>
/// The window handle is the part that is easy to get wrong: a picker created in a desktop app has
/// no window of its own to be modal to, and without InitializeWithWindow it throws rather than
/// appearing. That one call is most of the reason this lives behind a port.
/// </remarks>
public sealed class FileOpenService : IFileOpenService
{
    private readonly IMainWindowProvider _windows;

    public FileOpenService(IMainWindowProvider windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows;
    }

    public async Task<string?> PickFileAsync(string fileTypeLabel, string extension)
    {
        if (_windows.MainWindow is not { } window)
        {
            return null;
        }

        var picker = new FileOpenPicker {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(extension);

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();

        return file?.Path;
    }

    public async Task<string?> PickFolderAsync()
    {
        if (_windows.MainWindow is not { } window)
        {
            return null;
        }

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };

        // A folder picker with no filter shows nothing at all, which reads as a broken dialog.
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();

        return folder?.Path;
    }
}
