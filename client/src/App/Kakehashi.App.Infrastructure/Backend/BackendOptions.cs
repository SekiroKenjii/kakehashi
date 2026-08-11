namespace Kakehashi.App.Infrastructure.Backend {
  /// <summary>
  /// Strongly-typed configuration for the backend client, bound from the <c>Backend</c> section of
  /// <c>appsettings.json</c>. <see cref="Protocol"/> selects which transport implementation of
  /// <see cref="Contracts.IBackendClient"/> is registered, so switching HTTP &lt;-&gt; gRPC is a
  /// configuration change, not a code change.
  /// </summary>
  public sealed class BackendOptions {
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Backend";

    /// <summary>Base address of the backend service (e.g. <c>https://localhost:5001</c>).</summary>
    public string BaseAddress { get; set; } = "https://localhost:5001";

    /// <summary>Transport for reaching the backend. Defaults to <see cref="BackendProtocol.Http"/>.</summary>
    public BackendProtocol Protocol { get; set; } = BackendProtocol.Http;

    /// <summary>Per-request timeout, in seconds. Applies to the HTTP transport.</summary>
    public int TimeoutSeconds { get; set; } = 30;
  }
}
