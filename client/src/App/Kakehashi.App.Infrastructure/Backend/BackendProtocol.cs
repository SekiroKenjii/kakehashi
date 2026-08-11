namespace Kakehashi.App.Infrastructure.Backend {
  // The wire protocol the app uses to talk to the separate backend service.
  public enum BackendProtocol {
    // REST/JSON over HTTP using System.Net.Http.HttpClient.
    Http,

    // gRPC over HTTP/2 using Grpc.Net.Client.
    Grpc,
  }
}
