using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services.Platform {
  public interface IDialogService : IUiContractService, ISingletonDependency {
    Task ShowMessageAsync(string title, string message, string closeText = "OK");

    // True means the primary action was chosen, not merely that the dialog closed.
    Task<bool> ShowConfirmAsync(
        string title, string message, string primaryText = "Yes", string closeText = "No");

    // Null when dismissed, rather than an empty string: "typed nothing" and "changed my mind" are
    // different answers, and only one of them should go on to be validated.
    Task<string?> ShowPromptAsync(
        string title, string label, string initialValue = "", string primaryText = "OK");

    // Answers come back in field order, or null when cancelled. A field marked secret is rendered
    // masked; secret is part of the tuple rather than a separate overload because the dialogs that
    // need it are mixed - "Add user" asks for an email, a name and a password in one breath - and
    // an overload would have meant either two dialogs or a second parameter nobody remembers.
    Task<IReadOnlyList<string>?> ShowInputsAsync(
        string title,
        string primaryText,
        params (string Label, string InitialValue, bool IsSecret)[] fields);
  }
}
