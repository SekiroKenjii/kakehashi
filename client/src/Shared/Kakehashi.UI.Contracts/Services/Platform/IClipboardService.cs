namespace Kakehashi.UI.Contracts.Services.Platform {
  // A port for one line of WinRT, so a view model can offer "copy this" without touching
  // Windows.ApplicationModel.DataTransfer, a UI-thread API that would drag the whole clipboard
  // surface into every test that constructs the view model.
  public interface IClipboardService : IUiContractService, ISingletonDependency {
    void SetText(string text);
  }
}
