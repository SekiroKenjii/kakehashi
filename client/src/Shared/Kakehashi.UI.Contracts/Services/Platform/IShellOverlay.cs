using System;

namespace Kakehashi.UI.Contracts.Services.Platform {
  // Dims and blurs the main window's content while a modal interaction owns the user's attention.
  public interface IShellOverlay : IUiContractService {
    // Dispose the handle to remove the overlay. A no-op when the main window does not exist yet.
    IDisposable Show();
  }
}
