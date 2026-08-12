namespace Kakehashi.App.Infrastructure.Backend {
  // Protocol selects which transport implementation of Contracts.IBackendClient is registered, so
  // switching HTTP <-> gRPC is a configuration change, not a code change.
  public sealed class BackendOptions {
    public const string SectionName = "Backend";

    public string BaseAddress { get; set; } = "https://localhost:5001";

    public BackendProtocol Protocol { get; set; } = BackendProtocol.Http;

    // HTTP transport only; the gRPC client ignores it.
    public int TimeoutSeconds { get; set; } = 30;
  }
}
