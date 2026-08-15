using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Security;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Backend;

/// <summary>
/// Adds an <c>Authorization: Bearer</c> header to outbound HTTP backend calls, using the token
/// supplied by <see cref="IAccessTokenProvider"/>. When the provider returns no token the request
/// is sent unauthenticated, so this handler is harmless when authentication is disabled.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;

    public BearerTokenHandler(IAccessTokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
