using System.Globalization;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Domain.Notes {
  // A note as the user has typed it, once it is known to be worth sending.
  //
  // The server owns notes: it assigns their identifiers, stores them, and has the final word on
  // whether one is valid. So this is not an entity, and the client deliberately has no
  // Note aggregate to pretend otherwise.
  //
  // What the client does own is the moment before the request. Re-stating the rules here buys
  // immediate feedback on an empty title instead of a round trip, and it costs nothing to keep
  // honest because the server re-checks everything anyway. The rules are duplicated on purpose;
  // the two copies serve different masters. This one exists for speed, the server's for truth,
  // and if they ever disagree the server wins.
  public sealed record NoteDraft {
    // Matches domain.MaxTitleLength on the server and the NVARCHAR(120) column
    // behind it. Changing one without the others turns a helpful message into a failed request.
    public const int MaxTitleLength = 120;

    private NoteDraft(string title, string body) {
      Title = title;
      Body = body;
    }

    // Trimmed, non-empty, at most MaxTitleLength characters.
    public string Title { get; }

    // Free text. May be empty: a note is a title with optional contents.
    public string Body { get; }

    // Validates what the user typed, or explains why it cannot be sent.
    public static Result<NoteDraft> Create(string? title, string? body) {
      var trimmed = (title ?? string.Empty).Trim();

      if (trimmed.Length == 0) {
        return Result.Failure<NoteDraft>(NotesErrors.TitleRequired);
      }

      // Text elements, not chars: an emoji or a Vietnamese letter with a stacked diacritic can be
      // several UTF-16 chars, and counting those would reject a title the server accepts.
      var elements = new StringInfo(trimmed).LengthInTextElements;
      if (elements > MaxTitleLength) {
        return Result.Failure<NoteDraft>(NotesErrors.TitleTooLong);
      }

      return Result.Success(new NoteDraft(trimmed, body ?? string.Empty));
    }
  }
}
