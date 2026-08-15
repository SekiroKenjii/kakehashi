using Microsoft.UI.Xaml;

namespace Kakehashi.UI.Contracts.Services.Platform;

/// <summary>
/// Exposes the application's main window to modules that need to position or own secondary
/// windows (e.g. a modal sign-in window centered over the shell).
/// </summary>
public interface IMainWindowProvider : IUiContractService
{
    /// <summary>The main window, or <see langword="null"/> until the shell has been created.</summary>
    Window? MainWindow { get; }
}
