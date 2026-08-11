namespace Kakehashi.App.Infrastructure.Backend {
  // Strongly-typed configuration for the backend client, bound from the Backend section of
  // appsettings.json. Protocol selects which transport implementation of
  // Contracts.IBackendClient is registered, so switching HTTP &lt;-&gt; gRPC is a
  // configuration change, not a code change.
  public sealed class BackendOptions {
    // The configuration section these options bind to.
    public const string SectionName = "Backend";

    // Base address of the backend service (e.g. https://localhost:5001).
    public string BaseAddress { get; set; } = "https://localhost:5001";

    // Transport used to reach the backend. Defaults to BackendProtocol.Http.
    public BackendProtocol Protocol { get; set; } = BackendProtocol.Http;

    // Per-request timeout, in seconds. Applies to the HTTP transport.
    public int TimeoutSeconds { get; set; } = 30;
  }
}
