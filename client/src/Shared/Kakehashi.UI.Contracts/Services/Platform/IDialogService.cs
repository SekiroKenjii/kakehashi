using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services.Platform;

/// <summary>Shows modal content dialogs anchored to the main window.</summary>
public interface IDialogService : IUiContractService, ISingletonDependency
{
    /// <summary>Shows an informational dialog with a single dismiss button.</summary>
    Task ShowMessageAsync(string title, string message, string closeText = "OK");

    /// <summary>Shows a confirmation dialog.</summary>
    /// <returns><see langword="true"/> if the user chose the primary action.</returns>
    Task<bool> ShowConfirmAsync(
        string title, string message, string primaryText = "Yes", string closeText = "No");

    /// <summary>Asks for one line of text.</summary>
    /// <returns>
    /// What was typed, or <see langword="null"/> if the dialog was dismissed. Null rather than an
    /// empty string, because "typed nothing" and "changed my mind" are different answers and only
    /// one of them should go on to be validated.
    /// </returns>
    Task<string?> ShowPromptAsync(
        string title, string label, string initialValue = "", string primaryText = "OK");

    /// <summary>
    /// Asks for several values at once and returns them in field order, or null when cancelled.
    /// </summary>
    /// <remarks>
    /// A field marked secret is rendered masked. Secrecy is per field rather than per call because
    /// one dialog mixes plain and secret fields ("Add user": email, name, password).
    /// </remarks>
    Task<IReadOnlyList<string>?> ShowInputsAsync(
        string title,
        string primaryText,
        params (string Label, string InitialValue, bool IsSecret)[] fields);
}
