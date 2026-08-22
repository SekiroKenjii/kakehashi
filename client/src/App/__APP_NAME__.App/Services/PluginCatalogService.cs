using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using __ROOT_NAMESPACE__.SharedKernel;
using PluginsV1 = __ROOT_NAMESPACE__.Plugins.V1;

namespace __ROOT_NAMESPACE__.App.Services;

/// <summary>One offering in the catalog, together with the newest version still on offer.</summary>
/// <param name="Publisher">
/// Who the deployment says publishes this. Not a signature — what signed the assemblies is on the
/// artifact, and the client judges that for itself once the package is open.
/// </param>
/// <param name="SHA256">
/// What the catalog says the archive hashes to, which is what a download is checked against.
/// </param>
public sealed record CatalogPlugin(
    string PluginID,
    string DisplayName,
    string Description,
    string Publisher,
    string Version,
    string MinHostSdk,
    long SizeInBytes,
    string SHA256,
    DateTimeOffset PublishedAt);

/// <summary>
/// The catalog a deployment offers, as a port.
/// </summary>
/// <remarks>
/// An interface for the reason the other two gateways are: the generated client returns
/// <c>AsyncUnaryCall</c> and <c>AsyncServerStreamingCall</c>, neither of which a substitute
/// constructs cleanly, so the view model is tested against this instead.
/// </remarks>
public interface IPluginCatalogService
{
    Task<Result<IReadOnlyList<CatalogPlugin>>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Writes one version to <paramref name="targetPath"/>, or refuses and leaves nothing.</summary>
    Task<Result> DownloadAsync(
        CatalogPlugin plugin, string targetPath, CancellationToken cancellationToken);

    /// <summary>Tells the catalog this account installed a version.</summary>
    Task<Result> ReportInstalledAsync(
        string pluginID, string version, CancellationToken cancellationToken);
}

/// <summary>
/// The host's client for the plugin catalog.
/// </summary>
/// <remarks>
/// The host's rather than a module's, for the same reason the access and navigation gateways are:
/// it feeds a screen that governs every module, and a feature module that owned it would couple the
/// composition to whichever module happened to hold it.
/// </remarks>
public sealed class PluginCatalogService : IPluginCatalogService
{
    private readonly PluginsV1.PluginService.PluginServiceClient _plugins;

    public PluginCatalogService(PluginsV1.PluginService.PluginServiceClient plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        _plugins = plugins;
    }

    public async Task<Result<IReadOnlyList<CatalogPlugin>>> ListAsync(CancellationToken ct)
    {
        try
        {
            var reply = await _plugins
                .ListPluginsAsync(new PluginsV1.ListPluginsRequest(), cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            // The two lists are parallel by contract, and a reply that disagrees with itself is
            // truncated rather than indexed past: a row without its version has nothing to install.
            IReadOnlyList<CatalogPlugin> rows = [.. reply.Plugins.Zip(reply.Latest, Row)];

            return Result.Success(rows);
        }
        catch (RpcException exception)
        {
            return Result.Failure<IReadOnlyList<CatalogPlugin>>(ToError(exception));
        }
    }

    public async Task<Result> DownloadAsync(
        CatalogPlugin plugin, string targetPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);

        try
        {
            var digest = await WriteAsync(plugin, targetPath, ct).ConfigureAwait(false);

            // Checked before anything opens the archive: bytes that are not the ones the catalog
            // vouched for are not this package, whatever its name says.
            if (!digest.Equals(plugin.SHA256, StringComparison.OrdinalIgnoreCase))
            {
                Discard(targetPath);

                return Result.Failure(new Error(
                    "Plugin.Download.DigestMismatch",
                    $"What arrived is not what the catalog published for {plugin.DisplayName} "
                        + $"{plugin.Version}, so it was thrown away."));
            }

            return Result.Success();
        }
        catch (RpcException exception)
        {
            Discard(targetPath);

            return Result.Failure(ToError(exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(targetPath);

            return Result.Failure(new Error("Plugin.Download.Unwritable", exception.Message));
        }
    }

    public async Task<Result> ReportInstalledAsync(
        string pluginID, string version, CancellationToken ct)
    {
        try
        {
            _ = await _plugins
                .ReportInstalledAsync(
                    new PluginsV1.ReportInstalledRequest {
                        PluginId = pluginID,
                        Version = version,
                        Source = PluginsV1.InstallSource.Catalog,
                    },
                    cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            return Result.Success();
        }
        catch (RpcException exception)
        {
            return Result.Failure(ToError(exception));
        }
    }

    private static CatalogPlugin Row(PluginsV1.Plugin plugin, PluginsV1.PluginVersion version)
    {
        return new CatalogPlugin(
            plugin.PluginId,
            plugin.DisplayName,
            plugin.Description,
            plugin.Publisher,
            version.Version,
            version.MinHostSdk,
            version.SizeInBytes,
            version.Sha256,
            version.PublishedAt?.ToDateTimeOffset() ?? default);
    }

    /// <summary>Streams a version to disk, hashing it on the way through.</summary>
    /// <remarks>
    /// Hashed while it is written rather than read back afterwards, which is what keeps a package
    /// larger than memory from being held whole on either side of the wire.
    /// </remarks>
    private async Task<string> WriteAsync(
        CatalogPlugin plugin, string targetPath, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var call = _plugins.DownloadPluginVersion(
            new PluginsV1.DownloadPluginVersionRequest {
                PluginId = plugin.PluginID,
                Version = plugin.Version,
            },
            cancellationToken: ct);

        using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        await foreach (var message in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
            hash.AppendData(message.Chunk.Span);
            await file.WriteAsync(message.Chunk.Memory, ct).ConfigureAwait(false);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A half-written download that survives is overwritten by the next attempt at the same
            // version, and is never installed from: only a verified one reaches the installer.
        }
    }

    /// <summary>Turns a status into an error carrying the server's own sentence.</summary>
    private static Error ToError(RpcException exception)
    {
        var detail = exception.Status.Detail ?? string.Empty;

        // A route-gate refusal never carries the server's words: it answers with a plain HTTP 403
        // before Connect sees the request, so the transport's own text is all that arrives.
        if (detail.Length == 0
            || detail.StartsWith("Bad gRPC response", StringComparison.Ordinal))
        {
            detail = exception.StatusCode switch {
                StatusCode.PermissionDenied =>
                    "You no longer have access to the plugin catalog. Ask an administrator to "
                        + "restore it.",
                StatusCode.Unauthenticated => "Your session has ended. Sign in again.",
                StatusCode.Unavailable => "The plugin catalog could not be reached.",
                _ => "The server could not complete that request.",
            };
        }

        return new Error(exception.StatusCode.ToString(), detail);
    }
}
