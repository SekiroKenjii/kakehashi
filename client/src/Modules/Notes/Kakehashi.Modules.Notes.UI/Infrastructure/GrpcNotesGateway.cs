using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Logging;
using NotesV1 = Kakehashi.Notes.V1;

namespace Kakehashi.Modules.Notes.UI.Infrastructure {
  // The gRPC adapter behind INotesGateway. It is the only class in the module that
  // knows the wire exists: it maps between the generated messages and the application's DTOs, and
  // turns transport failures into Result failures.
  //
  // The alias is NotesV1 rather than Notes because inside a Kakehashi.*
  // namespace the identifier Notes binds to the enclosing Kakehashi.Modules.Notes
  // namespace before a using alias is ever considered.
  public sealed partial class GrpcNotesGateway : INotesGateway {
    private readonly NotesV1.NotesService.NotesServiceClient _client;
    private readonly ILogger<GrpcNotesGateway> _logger;

    public GrpcNotesGateway(
        NotesV1.NotesService.NotesServiceClient client, ILogger<GrpcNotesGateway> logger) {
      ArgumentNullException.ThrowIfNull(client);
      ArgumentNullException.ThrowIfNull(logger);
      _client = client;
      _logger = logger;
    }

    public async Task<Result<IReadOnlyList<NoteDto>>> ListAsync(
        CancellationToken cancellationToken) {
      try {
        var reply = await _client
            .ListNotesAsync(new NotesV1.ListNotesRequest(), cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        var notes = new List<NoteDto>(reply.Notes.Count);
        foreach (var note in reply.Notes) {
          notes.Add(ToDto(note));
        }
        return Result.Success<IReadOnlyList<NoteDto>>(notes);
      } catch (RpcException exception) {
        return Result.Failure<IReadOnlyList<NoteDto>>(Translate(exception, nameof(ListAsync)));
      }
    }

    public async Task<Result<NoteDto>> CreateAsync(
        NoteDraft draft, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(draft);
      try {
        var reply = await _client
            .CreateNoteAsync(
                new NotesV1.CreateNoteRequest { Title = draft.Title, Body = draft.Body },
                cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        return Result.Success(ToDto(reply.Note));
      } catch (RpcException exception) {
        return Result.Failure<NoteDto>(Translate(exception, nameof(CreateAsync)));
      }
    }

    public async Task<Result<NoteDto>> UpdateAsync(
        long id, NoteDraft draft, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(draft);
      try {
        var reply = await _client
            .UpdateNoteAsync(
                new NotesV1.UpdateNoteRequest { Id = id, Title = draft.Title, Body = draft.Body },
                cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        return Result.Success(ToDto(reply.Note));
      } catch (RpcException exception) {
        return Result.Failure<NoteDto>(Translate(exception, nameof(UpdateAsync)));
      }
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken cancellationToken) {
      try {
        await _client
            .DeleteNoteAsync(
                new NotesV1.DeleteNoteRequest { Id = id }, cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        return Result.Success();
      } catch (RpcException exception) {
        return Result.Failure(Translate(exception, nameof(DeleteAsync)));
      }
    }

    private static NoteDto ToDto(NotesV1.Note note) {
      return new NoteDto(
          note.Id,
          note.Title,
          note.Body,
          note.CreatedAt.ToDateTimeOffset(),
          note.UpdatedAt.ToDateTimeOffset());
    }

    // Turns a gRPC status into an error the user can be shown.
    private Error Translate(RpcException exception, string operation) {
      switch (exception.StatusCode) {
        case StatusCode.InvalidArgument:
          // The server's validation message is written for a user and is the authoritative one —
          // it is what the domain returned. Passing it through rather than substituting a generic
          // string is the difference between "Titles are limited to 120 characters" and
          // "Something went wrong".
          LogRejected(operation, exception.Status.Detail);
          return new Error(NotesErrors.TitleRequired.Code, exception.Status.Detail);

        case StatusCode.NotFound:
          return NotesErrors.NotFound;

        case StatusCode.PermissionDenied:
          // The server gates whole modules, so this is never about one note: an administrator has
          // not assigned this account the Notes module. Kept out of the catch-all below because it
          // is the one failure here a user can actually do something about.
          return NotesErrors.NotAssigned;

        default:
          // Everything else is the network, the server, or a bug — none of which the user can act
          // on beyond trying again. The detail goes to the log, not the screen.
          LogFailed(operation, exception.StatusCode, exception);
          return NotesErrors.RequestFailed;
      }
    }

    [LoggerMessage(
        Level = LogLevel.Information, Message = "Notes {Operation} was rejected: {Detail}")]
    private partial void LogRejected(string operation, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notes {Operation} failed with {Status}.")]
    private partial void LogFailed(string operation, StatusCode status, Exception exception);
  }
}
