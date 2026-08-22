using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

/// <summary>Asks the user for a file the app is about to read.</summary>
/// <remarks>
/// A port for the same reason its saving counterpart is one: a view model that opens something can
/// then be tested without a picker, and the one place that knows about <c>FileOpenPicker</c> and
/// window handles stays in the host.
/// </remarks>
public interface IFileOpenService : IUiContractService, ISingletonDependency
{
    /// <summary>
    /// Shows an open dialog and returns the chosen path, or null when the user cancelled.
    /// </summary>
    /// <param name="fileTypeLabel">What the type is called in the dialog, e.g. "Plugin package".</param>
    /// <param name="extension">The extension, dot included, e.g. ".plugin".</param>
    Task<string?> PickFileAsync(string fileTypeLabel, string extension);
}
