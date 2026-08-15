using System;

namespace __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

/// <summary>
/// Dims and blurs the main window's content while a modal interaction (e.g. a forced re-sign-in)
/// owns the user's attention.
/// </summary>
public interface IShellOverlay : IUiContractService
{
    /// <summary>
    /// Shows the overlay over the main window's content. Dispose the returned handle to remove it.
    /// A no-op when the main window does not exist yet.
    /// </summary>
    IDisposable Show();
}
