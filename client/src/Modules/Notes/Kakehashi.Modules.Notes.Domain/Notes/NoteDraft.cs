using System.Globalization;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Domain.Notes {
  /// <summary>
  /// A note as the user has typed it, once it is known to be worth sending.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The server owns notes: it assigns their identifiers, stores them, and has the final word on
  /// whether one is valid. So this is not an entity, and the client deliberately has no
  /// <c>Note</c> aggregate to pretend otherwise.
  /// </para>
  /// <para>
  /// What the client does own is the moment before the request. Re-stating the rules here buys
  /// immediate feedback on an empty title instead of a round trip, and it costs nothing to keep
  /// honest because the server re-checks everything anyway. The rules are duplicated on purpose;
  /// the two copies serve different masters. This one exists for speed, the server's for truth,
  /// and if they ever disagree the server wins.
  /// </para>
  /// </remarks>
  public sealed record NoteDraft {
    /// <summary>
    /// Matches <c>domain.MaxTitleLength</c> on the server and the <c>NVARCHAR(120)</c> column
    /// behind it. Changing one without the others turns a helpful message into a failed request.
    /// </summary>
    public const int MaxTitleLength = 120;

    private NoteDraft(string title, string body) {
      Title = title;
      Body = body;
    }

    /// <summary>Trimmed, non-empty, at most <see cref="MaxTitleLength"/> characters.</summary>
    public string Title { get; }

    /// <summary>Free text. May be empty: a note is a title with optional contents.</summary>
    public string Body { get; }

    /// <summary>Validates what the user typed, or explains why it cannot be sent.</summary>
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
