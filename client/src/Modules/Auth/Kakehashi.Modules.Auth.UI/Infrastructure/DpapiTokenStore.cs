using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Application.Abstractions;

namespace Kakehashi.Modules.Auth.UI.Infrastructure {
  // Access and id tokens are deliberately never written to disk; only the refresh token is.
  public sealed class DpapiTokenStore : ITokenStore {
    private static readonly byte[] _entropy =
        Encoding.UTF8.GetBytes("Kakehashi.Auth.RefreshToken.v1");

    private readonly string _path;

    public DpapiTokenStore() {
      var directory = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kakehashi");
      Directory.CreateDirectory(directory);
      _path = Path.Combine(directory, "auth.tokens");
    }

    public Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken) {
      try {
        if (!File.Exists(_path)) {
          return Task.FromResult<string?>(null);
        }
        var protectedBytes = File.ReadAllBytes(_path);
        var bytes = ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
      } catch (Exception ex)
          when (ex is IOException or UnauthorizedAccessException or CryptographicException) {
        // Unreadable or tampered: treat as "no stored session" rather than failing startup.
        return Task.FromResult<string?>(null);
      }
    }

    public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken) {
      ArgumentException.ThrowIfNullOrEmpty(refreshToken);
      var protectedBytes = ProtectedData.Protect(
          Encoding.UTF8.GetBytes(refreshToken), _entropy, DataProtectionScope.CurrentUser);
      try {
        File.WriteAllBytes(_path, protectedBytes);
      } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // Best-effort persistence; failure just means the user re-authenticates next launch.
      }
      return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken) {
      try {
        if (File.Exists(_path)) {
          File.Delete(_path);
        }
      } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // Ignore: a leftover encrypted token is unusable without the user's DPAPI key.
      }
      return Task.CompletedTask;
    }
  }
}
