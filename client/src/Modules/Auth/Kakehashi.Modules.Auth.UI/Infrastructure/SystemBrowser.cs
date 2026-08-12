using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel.OidcClient.Browser;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  // RFC 8252 native-app pattern: the default system browser plus a loopback HttpListener for the
  // redirect. An embedded WebView is deliberately not used.
  public sealed class SystemBrowser : IBrowser {
    private readonly string _redirectUri;
    private volatile string? _currentStartUrl;

    public SystemBrowser(string redirectUri) {
      ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
      _redirectUri = redirectUri.EndsWith('/') ? redirectUri : redirectUri + "/";
    }

    public async Task<BrowserResult> InvokeAsync(
        BrowserOptions options, CancellationToken cancellationToken = default) {
      ArgumentNullException.ThrowIfNull(options);

      using var listener = new HttpListener();
      listener.Prefixes.Add(_redirectUri);
      listener.Start();

      try {
        _currentStartUrl = options.StartUrl;
        LaunchSystemBrowser(options.StartUrl);

        HttpListenerContext context;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Timeout > TimeSpan.Zero) {
          timeoutCts.CancelAfter(options.Timeout);
        }

        using (timeoutCts.Token.Register(listener.Stop)) {
          try {
            context = await listener.GetContextAsync().ConfigureAwait(false);
          } catch (Exception) when (cancellationToken.IsCancellationRequested) {
            return new BrowserResult { ResultType = BrowserResultType.UserCancel };
          } catch (Exception) when (timeoutCts.IsCancellationRequested) {
            return new BrowserResult {
              ResultType = BrowserResultType.Timeout,
              Error = $"authentication timed out after {options.Timeout.TotalSeconds:F0}s; "
                  + "the callback was not received",
            };
          }
        }

        var result = context.Request.Url is { } url
            ? new BrowserResult { ResultType = BrowserResultType.Success, Response = url.AbsoluteUri }
            : new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = "Missing redirect URL." };

        await WriteClosePageAsync(context).ConfigureAwait(false);
        return result;
      } finally {
        _currentStartUrl = null;
        if (listener.IsListening) {
          listener.Stop();
        }
      }
    }

    // For when the user closed the browser tab while the flow was still waiting on the callback.
    public bool TryReopen() {
      if (_currentStartUrl is not { } url) {
        return false;
      }

      LaunchSystemBrowser(url);
      return true;
    }

    private static void LaunchSystemBrowser(string url) {
      // UseShellExecute is what hands the URL to the OS default browser.
      var startInfo = new ProcessStartInfo { FileName = url, UseShellExecute = true };
      using var process = Process.Start(startInfo);
    }

    private static async Task WriteClosePageAsync(HttpListenerContext context) {
      const string html =
          "<!doctype html><html><head><meta charset=\"utf-8\"/><title>Done</title></head>" +
          "<body style=\"font-family:Segoe UI,sans-serif;text-align:center;margin-top:4rem\">" +
          "<h2>All set</h2><p>You can close this window and return to the app.</p></body></html>";
      var buffer = Encoding.UTF8.GetBytes(html);
      context.Response.ContentType = "text/html; charset=utf-8";
      context.Response.ContentLength64 = buffer.Length;
      await context.Response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
      context.Response.OutputStream.Close();
    }
  }
}
