using System.Globalization;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Domain.Notes {
  // The server owns notes: it assigns their identifiers, stores them, and has the final word on
  // whether one is valid. So this is not an entity, and the client deliberately has no Note
  // aggregate to pretend otherwise.
  //
  // The rules are duplicated from the server on purpose: this copy buys immediate feedback on an
  // empty title instead of a round trip, the server's is the one that decides, and if they ever
  // disagree the server wins.
  public sealed record NoteDraft {
    // Matches domain.MaxTitleLength on the server and the NVARCHAR(120) column behind it. Changing
    // one without the others turns a helpful message into a failed request.
    public const int MaxTitleLength = 120;

    private NoteDraft(string title, string body) {
      Title = title;
      Body = body;
    }

    // Trimmed, non-empty, at most MaxTitleLength characters.
    public string Title { get; }

    // May be empty: a note is a title with optional contents.
    public string Body { get; }

    public static Result<NoteDraft> Create(string? title, string? body) {
      var trimmed = (title ?? string.Empty).Trim();

      if (trimmed.Length == 0) {
        return Result.Failure<NoteDraft>(NotesErrors.TitleRequired);
      }

      // Text elements, not chars: an emoji or a Vietnamese letter with a stacked diacritic is
      // several UTF-16 chars, and counting those would reject a title the server accepts.
      var elements = new StringInfo(trimmed).LengthInTextElements;
      if (elements > MaxTitleLength) {
        return Result.Failure<NoteDraft>(NotesErrors.TitleTooLong);
      }

      return Result.Success(new NoteDraft(trimmed, body ?? string.Empty));
    }
  }
}
