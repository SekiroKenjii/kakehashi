using System;

namespace Kakehashi.Modules.Notes.Application.Notes {
  // Title is a single line and never empty; the timestamps are UTC.
  public sealed record NoteDto(
      long Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
