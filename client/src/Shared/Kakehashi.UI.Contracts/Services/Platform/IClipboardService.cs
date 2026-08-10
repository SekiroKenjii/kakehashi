namespace Kakehashi.UI.Contracts.Services.Platform {
  /// <summary>Puts text on the system clipboard.</summary>
  /// <remarks>
  /// A port for one line of WinRT, so a view model can offer "copy this" without touching
  /// <c>Windows.ApplicationModel.DataTransfer</c> — which is a UI-thread API and would drag the
  /// whole clipboard surface into every test that constructs the view model.
  /// </remarks>
  public interface IClipboardService : IUiContractService, ISingletonDependency {
    /// <summary>Replaces the clipboard's contents with <paramref name="text"/>.</summary>
    void SetText(string text);
  }
}
