using System;
using System.Net.Http.Headers;
using Kakehashi.Modules.Auth.UI.Infrastructure;
using Xunit;

namespace Kakehashi.Modules.Auth.UI.Tests.Infrastructure {
  /// <summary>
  /// Unit tests for <see cref="InAppAuthenticator"/>'s device label — the one part of the adapter
  /// that runs without a server, and the one the Account page's session list depends on.
  /// </summary>
  public sealed class InAppAuthenticatorTests {
    [Fact]
    public void DeviceLabel_NamesTheProductAndTheMachine() {
      var label = InAppAuthenticator.DeviceLabel();

      Assert.StartsWith("Kakehashi-Desktop/", label, StringComparison.Ordinal);
      Assert.Contains($"({Environment.MachineName})", label, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceLabel_IsAWellFormedUserAgent() {
      // The header is added without validation so a hostile machine name cannot break sign-in.
      // That makes it worth proving the ordinary case still parses as a real User-Agent rather
      // than silently reaching the server as something no proxy or log will understand.
      Assert.True(ProductInfoHeaderValue.TryParse(
          InAppAuthenticator.DeviceLabel().Split(' ')[0], out _));
    }
  }
}
