namespace __ROOT_NAMESPACE__.App.Infrastructure.Backend;

/// <summary>The wire protocol the app uses to talk to the separate backend service.</summary>
public enum BackendProtocol
{
    /// <summary>REST/JSON over HTTP using <see cref="System.Net.Http.HttpClient"/>.</summary>
    Http,

    /// <summary>gRPC over HTTP/2 using <c>Grpc.Net.Client</c>.</summary>
    Grpc,
}
