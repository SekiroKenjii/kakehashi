using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.App.Infrastructure.Backend;
using __ROOT_NAMESPACE__.Application.Abstractions.Security;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Tests;

public sealed class BearerTokenHandlerTests
{
    [Fact]
    public async Task SendAsync_WithToken_AddsBearerAuthorizationHeader()
    {
        var request = await SendThroughHandler(new StubTokenProvider("abc123"));

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("abc123", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_WithoutToken_DoesNotAddAuthorizationHeader()
    {
        var request = await SendThroughHandler(new StubTokenProvider(token: null));

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task NullAccessTokenProvider_ReturnsNull()
    {
        var token = await new NullAccessTokenProvider().GetAccessTokenAsync(CancellationToken.None);

        Assert.Null(token);
    }

    private static async Task<HttpRequestMessage> SendThroughHandler(IAccessTokenProvider provider)
    {
        var capturing = new CapturingHandler();
        using var handler = new BearerTokenHandler(provider) { InnerHandler = capturing };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/ping");

        await invoker.SendAsync(request, CancellationToken.None);

        return capturing.Request!;
    }

    private sealed class StubTokenProvider : IAccessTokenProvider
    {
        private readonly string? _token;

        public StubTokenProvider(string? token)
        {
            _token = token;
        }

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_token);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
