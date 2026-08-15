using System;

namespace Kakehashi.Modules.Notes.Application.Notes;

/// <summary>A note as the server reports it.</summary>
/// <param name="Id">Server-assigned identifier.</param>
/// <param name="Title">A single line, never empty.</param>
/// <param name="Body">Free text, possibly empty.</param>
/// <param name="CreatedAt">When the note was first stored, in UTC.</param>
/// <param name="UpdatedAt">When it last changed, in UTC.</param>
public sealed record NoteDto(
    long Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
