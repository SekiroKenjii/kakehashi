using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.IntegrationTests {
  /// <summary>
  /// Stands in for the server, reproducing the behaviour the contract promises: server-assigned
  /// ids, newest-first ordering, and a delete that succeeds even when there is nothing to delete.
  /// </summary>
  /// <remarks>
  /// If the real service ever stops behaving this way, these tests keep passing and the
  /// end-to-end run fails; that division of labour is deliberate, because only this suite can run
  /// on every commit.
  /// </remarks>
  internal sealed class InMemoryNotesGateway : INotesGateway {
    private readonly Dictionary<long, NoteDto> _notes = [];
    private long _nextId = 1;

    public bool FailEverything { get; set; }

    public Task<Result<IReadOnlyList<NoteDto>>> ListAsync(CancellationToken cancellationToken) {
      if (FailEverything) {
        return Task.FromResult(
            Result.Failure<IReadOnlyList<NoteDto>>(NotesErrors.RequestFailed));
      }

      IReadOnlyList<NoteDto> ordered = [.. _notes.Values
          .OrderByDescending(note => note.UpdatedAt)
          .ThenByDescending(note => note.Id)];
      return Task.FromResult(Result.Success(ordered));
    }

    public Task<Result<NoteDto>> CreateAsync(NoteDraft draft, CancellationToken cancellationToken) {
      if (FailEverything) {
        return Task.FromResult(Result.Failure<NoteDto>(NotesErrors.RequestFailed));
      }

      var now = DateTimeOffset.UtcNow;
      var note = new NoteDto(_nextId++, draft.Title, draft.Body, now, now);
      _notes[note.Id] = note;
      return Task.FromResult(Result.Success(note));
    }

    public Task<Result<NoteDto>> UpdateAsync(
        long id, NoteDraft draft, CancellationToken cancellationToken) {
      if (FailEverything) {
        return Task.FromResult(Result.Failure<NoteDto>(NotesErrors.RequestFailed));
      }
      if (!_notes.TryGetValue(id, out var existing)) {
        return Task.FromResult(Result.Failure<NoteDto>(NotesErrors.NotFound));
      }

      var updated = existing with {
        Title = draft.Title,
        Body = draft.Body,
        UpdatedAt = DateTimeOffset.UtcNow,
      };
      _notes[id] = updated;
      return Task.FromResult(Result.Success(updated));
    }

    public Task<Result> DeleteAsync(long id, CancellationToken cancellationToken) {
      if (FailEverything) {
        return Task.FromResult(Result.Failure(NotesErrors.RequestFailed));
      }

      // Succeeds whether or not the note was there, matching the server.
      _notes.Remove(id);
      return Task.FromResult(Result.Success());
    }
  }
}
