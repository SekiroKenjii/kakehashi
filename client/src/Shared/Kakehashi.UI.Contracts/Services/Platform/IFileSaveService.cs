using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services.Platform {
  // A port so a view model that exports something can be tested without a picker, and so the one
  // place that knows about FileSavePicker and window handles stays in the host. The alternative was
  // a hardcoded folder: an export landing somewhere the user did not choose is a file they have to
  // be told the path of and then go and find.
  public interface IFileSaveService : IUiContractService, ISingletonDependency {
    // Null when the user cancelled. suggestedName carries its extension, and extension carries its
    // dot (".csv").
    Task<string?> PickSaveLocationAsync(
        string suggestedName, string fileTypeLabel, string extension);
  }
}
