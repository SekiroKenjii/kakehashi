namespace Kakehashi.App.Infrastructure.Backend {
  /// <summary>
  /// Strongly-typed configuration for the backend client, bound from the <c>Backend</c> section of
  /// <c>appsettings.json</c>. <see cref="Protocol"/> selects which transport implementation of
  /// <see cref="Contracts.IBackendClient"/> is registered, so switching HTTP &lt;-&gt; gRPC is a
  /// configuration change, not a code change.
  /// </summary>
  public sealed class BackendOptions {
    public const string SectionName = "Backend";

    public string BaseAddress { get; set; } = "https://localhost:5001";

    public BackendProtocol Protocol { get; set; } = BackendProtocol.Http;

    /// <summary>Applies only to the HTTP transport; the gRPC transport ignores it.</summary>
    public int TimeoutSeconds { get; set; } = 30;
  }
}
