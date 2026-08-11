using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Domain.Notes {
  // The errors the Notes module can return.
  public static class NotesErrors {
    public static readonly Error TitleRequired =
        new("Notes.Note.TitleRequired", "A note needs a title.");

    public static readonly Error TitleTooLong =
        new("Notes.Note.TitleTooLong",
            $"Titles are limited to {NoteDraft.MaxTitleLength} characters.");

    // The server could not be reached, or answered with something this client cannot act on.
    // Distinct from the validation errors above because the remedy is different: wait and retry,
    // rather than change what you typed.
    public static readonly Error RequestFailed =
        new("Notes.Gateway.RequestFailed", "Could not reach the notes service.");

    public static readonly Error NotFound =
        new("Notes.Note.NotFound", "That note no longer exists.");

    // The server refused because this account is not assigned the Notes module.
    public static readonly Error NotAssigned = new(
        "Notes.NotAssigned",
        "Your account is not assigned Notes. Ask an administrator for access.");
  }
}
