using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.Logging;
using AuthzV1 = Kakehashi.Authz.V1;

namespace Kakehashi.App.Services;

/// <summary>What the signed-in account may do, as this client understands it.</summary>
/// <remarks>
/// A port so view models can be tested without a server, and so every reader takes the one answer
/// from one place instead of asking the server separately.
/// </remarks>
public interface IPermissionService
{
    /// <summary>
    /// Whether the account holds the permission at all, at any scope.
    /// </summary>
    /// <remarks>
    /// Presentation only. Every route is checked server-side against the same table, so a client
    /// that never asked, or asked and lied to itself, is refused identically. What this buys is a
    /// hidden button instead of one that fails.
    /// </remarks>
    bool Allows(string permissionKey);

    /// <summary>
    /// How far the permission reaches: <c>own</c>, <c>team</c>, <c>all</c>, or empty when the
    /// account does not hold it.
    /// </summary>
    string ScopeOf(string permissionKey);

    /// <summary>Re-reads the account's grants from the server.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raised after <see cref="RefreshAsync"/> changes what the account may do.
    /// </summary>
    /// <remarks>
    /// Grants are resolved per request, so they can change under a screen that is already open —
    /// an administrator who just edited their own role is the ordinary case. A page that heard
    /// nothing would keep showing a working screen that answers 403 to everything.
    /// </remarks>
    event EventHandler? GrantsChanged;
}

/// <summary>
/// Asks the server what the account may do, and tells the registry which modules to lock.
/// </summary>
/// <remarks>
/// A host service rather than a feature module's, because what it feeds is the host's: the
/// registry governs every module, and a feature module that governed the others would reach
/// across the boundary the architecture tests exist to hold.
/// <para>
/// Module access is the ordinary permission <c>&lt;module&gt;.access</c>, so the lock on a page
/// and the refusal on a route read the same row. A separate module-assignment store would answer
/// "may this account use this module" with a table of its own, beside the permission table that
/// answers everything else — two systems, one question, and no reason for them to agree.
/// </para>
/// </remarks>
public sealed partial class PermissionService : IPermissionService
{
    private readonly AuthzV1.AuthzService.AuthzServiceClient _client;
    private readonly IModuleRegistry _registry;
    private readonly ILogger<PermissionService> _logger;

    private Dictionary<string, string> _grants = [];

    public PermissionService(
        AuthzV1.AuthzService.AuthzServiceClient client,
        IModuleRegistry registry,
        ILogger<PermissionService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _registry = registry;
        _logger = logger;
    }

    public event EventHandler? GrantsChanged;

    public bool Allows(string permissionKey)
    {
        return _grants.ContainsKey(permissionKey);
    }

    public string ScopeOf(string permissionKey)
    {
        return _grants.TryGetValue(permissionKey, out var scope) ? scope : string.Empty;
    }

    /// <summary>Fetches the grants and applies the module locks to the registry.</summary>
    /// <remarks>
    /// Failure leaves the previous answer standing rather than emptying it: an unreachable server
    /// must not lock a user out of a client the server is going to refuse anyway.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _client
                .ListMyGrantsAsync(
                    new AuthzV1.ListMyGrantsRequest(), cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            var grants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var grant in reply.Grants)
            {
                grants[grant.PermissionKey] = ToScopeName(grant.Scope);
            }
            var changed = grants.Count != _grants.Count
                || grants.Any(pair => !_grants.TryGetValue(pair.Key, out var scope)
                    || scope != pair.Value);
            _grants = grants;

            var withheld = new List<string>();
            foreach (var module in _registry.All)
            {
                var serverId = module.Descriptor.AssignmentId;
                if (serverId is not null && !grants.ContainsKey($"{serverId}.access"))
                {
                    withheld.Add(serverId);
                }
            }

            // Nothing is reported as granted: a grant means the account MAY use the module. Which are
            // attached stays the user's preference, so a permission never forces a page into the pane.
            _registry.SetAssignments(withheld, []);
            LogApplied(grants.Count, withheld.Count);

            if (changed)
            {
                GrantsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (RpcException exception)
            when (exception.StatusCode == StatusCode.Unauthenticated)
        {
            // Signing out revokes the token and the refresh that follows lands here. Keeping the prior
            // grants would draw a shell for somebody who has left; an error would log the ordinary.
            _grants = [];
            LogSignedOut();
        }
        catch (RpcException exception)
        {
            LogFailed(exception.StatusCode, exception);
        }
    }

    private static string ToScopeName(AuthzV1.Scope scope)
    {
        return scope switch {
            AuthzV1.Scope.Own => "own",
            AuthzV1.Scope.Team => "team",
            AuthzV1.Scope.All => "all",
            // Unspecified, or a scope this build predates. Empty reads as no reach, which is the
            // narrowing direction — the only safe one for a value nothing here understands.
            _ => string.Empty,
        };
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Permissions applied: {Granted} held, {Withheld} modules locked.")]
    private partial void LogApplied(int granted, int withheld);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Permissions could not be fetched ({Status}); the previous answer stands and "
            + "the server still refuses what it should.")]
    private partial void LogFailed(StatusCode status, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No signed-in caller; permissions cleared.")]
    private partial void LogSignedOut();
}
