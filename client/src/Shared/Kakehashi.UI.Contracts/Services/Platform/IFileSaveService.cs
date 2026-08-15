using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services.Platform;

/// <summary>Asks the user where to put a file the app is about to write.</summary>
/// <remarks>
/// A port so a view model that exports something can be tested without a picker, and so the one
/// place that knows about <c>FileSavePicker</c> and window handles stays in the host.
/// </remarks>
public interface IFileSaveService : IUiContractService, ISingletonDependency
{
    /// <summary>
    /// Shows a save dialog and returns the chosen path, or null when the user cancelled.
    /// </summary>
    /// <param name="suggestedName">The file name to offer, extension included.</param>
    /// <param name="fileTypeLabel">What the type is called in the dialog, e.g. "CSV file".</param>
    /// <param name="extension">The extension, dot included, e.g. ".csv".</param>
    Task<string?> PickSaveLocationAsync(
        string suggestedName, string fileTypeLabel, string extension);
}
