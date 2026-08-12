using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.Modules.Notes.UI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using NotesV1 = Kakehashi.Notes.V1;

namespace Kakehashi.Modules.Notes.UI.Tests.Infrastructure {
  // Drives the real GrpcNotesGateway against a running server, over real gRPC, into real SQL
  // Server. Everything else in this suite substitutes something; this is the one that would notice
  // a schema change, a broken migration, or a status code the client maps wrongly.
  //
  // Skipped unless KAKEHASHI_TEST_BACKEND names a server, so a plain dotnet test on a laptop with
  // nothing running still passes. Failing instead would train everyone to ignore a red suite,
  // which costs more than the coverage is worth. To run it:
  //
  //   docker compose up -d
  //   $env:KAKEHASHI_TEST_BACKEND = "http://localhost:8080"
  //   dotnet test tests/Kakehashi.Modules.Notes.UI.Tests
  public sealed class LiveNotesGatewayTests {
    private const string _addressVariable = "KAKEHASHI_TEST_BACKEND";

    private static string? Address() {
      var address = Environment.GetEnvironmentVariable(_addressVariable);
      return string.IsNullOrWhiteSpace(address) ? null : address;
    }

    // Plain HTTP/2 with no TLS: Grpc.Net.Client supports it directly on .NET 5 and later, which
    // lets the development stack run without certificates.
    private static GrpcNotesGateway CreateGateway(string address) {
      return new GrpcNotesGateway(
          new NotesV1.NotesService.NotesServiceClient(GrpcChannel.ForAddress(address)),
          NullLogger<GrpcNotesGateway>.Instance);
    }

    [Fact]
    public async Task FullLifecycle_RoundTripsThroughTheRealServer() {
      var address = Address();
      Assert.SkipWhen(address is null, $"{_addressVariable} is not set.");

      var gateway = CreateGateway(address!);
      var ct = CancellationToken.None;
      var title = $"live-test-{Guid.NewGuid():N}";

      var created = await gateway.CreateAsync(
          NoteDraft.Create($"  {title}  ", "body from the live test").Value, ct);
      Assert.True(created.IsSuccess, $"create failed: {created.Error.Message}");
      Assert.Equal(title, created.Value.Title);
      Assert.NotEqual(0, created.Value.Id);

      try {
        var listed = await gateway.ListAsync(ct);
        Assert.True(listed.IsSuccess);
        Assert.Contains(listed.Value, note => note.Id == created.Value.Id);
        // Newest-updated first is part of the contract, and the note just written is the newest.
        Assert.Equal(created.Value.Id, listed.Value[0].Id);

        var updated = await gateway.UpdateAsync(
            created.Value.Id, NoteDraft.Create($"{title} (edited)", "rewritten").Value, ct);
        Assert.True(updated.IsSuccess, $"update failed: {updated.Error.Message}");
        Assert.Equal("rewritten", updated.Value.Body);
        // The server preserves CreatedAt across a rewrite and stores timestamps at the column's
        // millisecond precision, so what comes back is exactly what is on disk.
        Assert.Equal(created.Value.CreatedAt, updated.Value.CreatedAt);
        Assert.True(updated.Value.UpdatedAt >= created.Value.UpdatedAt);
      } finally {
        Assert.True((await gateway.DeleteAsync(created.Value.Id, ct)).IsSuccess);
      }

      // Deleting something already gone succeeds, which is what makes a retry after a dropped
      // connection safe.
      Assert.True((await gateway.DeleteAsync(created.Value.Id, ct)).IsSuccess);
    }

    [Fact]
    public async Task MissingNote_MapsToTheNotFoundError() {
      var address = Address();
      Assert.SkipWhen(address is null, $"{_addressVariable} is not set.");

      var result = await CreateGateway(address!).UpdateAsync(
          long.MaxValue,
          NoteDraft.Create("nothing to update", string.Empty).Value,
          CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(NotesErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task BlankTitle_IsRejectedByTheServerToo() {
      var address = Address();
      Assert.SkipWhen(address is null, $"{_addressVariable} is not set.");

      // The generated client directly, not the gateway: NoteDraft makes an invalid title
      // unrepresentable, so this is the only way to ask what the server does when one arrives
      // anyway — the case that matters, because a future client might not be this one.
      var client = new NotesV1.NotesService.NotesServiceClient(GrpcChannel.ForAddress(address!));

      var failure = await Assert.ThrowsAsync<RpcException>(async () =>
          await client.CreateNoteAsync(
              new NotesV1.CreateNoteRequest { Title = "   ", Body = "x" }));

      Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
      // The server's message is written for a user, and the gateway passes it through verbatim.
      Assert.Equal("A note needs a title.", failure.Status.Detail);
    }
  }
}
