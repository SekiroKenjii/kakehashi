using System;

namespace Kakehashi.Modules.Notes.Application.Notes {
  // A note as the server reports it.
  // Id: Server-assigned identifier.
  // Title: A single line, never empty.
  // Body: Free text, possibly empty.
  // CreatedAt: When the note was first stored, in UTC.
  // UpdatedAt: When it last changed, in UTC.
  public sealed record NoteDto(
      long Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
