using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services.Platform {
  /// <summary>Asks the user where to put a file the app is about to write.</summary>
  /// <remarks>
  /// A port so a view model that exports something can be tested without a picker, and so the one
  /// place that knows about <c>FileSavePicker</c> and window handles stays in the host.
  /// <para>
  /// It exists because the alternative was a hardcoded folder. An export that lands somewhere the
  /// user did not choose and cannot open from the app is a file they have to be told the path of and
  /// then go and find — which is most of the work of exporting, left undone.
  /// </para>
  /// </remarks>
  public interface IFileSaveService : IUiContractService, ISingletonDependency {
    /// <summary>
    /// Shows a save dialog and returns the chosen path, or null when the user cancelled.
    /// </summary>
    /// <param name="suggestedName">The file name to offer, extension included.</param>
    /// <param name="fileTypeLabel">What the type is called in the dialog, e.g. "CSV file".</param>
    /// <param name="extension">The extension, dot included, e.g. ".csv".</param>
    Task<string?> PickSaveLocationAsync(
        string suggestedName, string fileTypeLabel, string extension);
  }
}
