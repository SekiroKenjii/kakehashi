namespace Kakehashi.UI.Contracts.Services.Platform {
  /// <summary>Puts text on the system clipboard.</summary>
  /// <remarks>
  /// A port so view models never touch <c>Windows.ApplicationModel.DataTransfer</c> — a UI-thread
  /// API that would otherwise enter every test constructing the view model.
  /// </remarks>
  public interface IClipboardService : IUiContractService, ISingletonDependency {
    /// <summary>Replaces the clipboard's contents with <paramref name="text"/>.</summary>
    void SetText(string text);
  }
}
