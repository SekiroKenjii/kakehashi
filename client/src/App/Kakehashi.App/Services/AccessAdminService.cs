using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Kakehashi.SharedKernel;
using AccountV1 = Kakehashi.Account.V1;
using AuthzV1 = Kakehashi.Authz.V1;

namespace Kakehashi.App.Services;

/// <param name="PermissionTotal">
/// How many permissions exist in the whole catalogue — the denominator of "22/34 perms" and the
/// role card's progress bar. Optional because the create/update replies do not carry it and
/// their callers reload immediately anyway.
/// </param>
public sealed record RoleRow(
    string Id, string Name, string Description, bool IsSystem,
    int PermissionCount, int AccountCount, int PermissionTotal = 0);

/// <param name="IsScoped">
/// Whether the module enforcing this permission actually narrows its queries on the grant's
/// scope. The own/team/all picker is offered only where it does.
/// </param>
public sealed record PermissionRow(
    string Key, string Name, string Description, string Category, bool IsHighRisk, bool IsScoped);

public sealed record GrantRow(string PermissionKey, string Scope);

/// <summary>The changes a save actually made, not what was submitted.</summary>
public sealed record SaveOutcome(int Granted, int Revoked, int Rescoped)
{
    public int Total => Granted + Revoked + Rescoped;
}

public sealed record AuditRow(
    DateTimeOffset OccurredAt, string ActorName, string Action, string RoleName,
    string PermissionKey, string Detail);

public sealed record SessionRow(
    string Id, string Client, string Device, string IpAddress,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);

public sealed record UserRow(
    string Id, string Email, string DisplayName, string Phone, string TeamId, bool IsActive,
    DateTimeOffset? LastSignInAt, DateTimeOffset CreatedAt, int ActiveSessionCount,
    IReadOnlyList<string> RoleNames);

/// <summary>
/// The administrative operations behind the two access screens, as a port.
/// </summary>
/// <remarks>
/// An interface so the view models can be tested without a server: the generated gRPC client
/// returns <c>AsyncUnaryCall</c>, which no substitute constructs cleanly. One port over two
/// services because one screen reads from both; callers must not encode which server module owns
/// which half.
/// </remarks>
public interface IAccessAdminService
{
    Task<Result<IReadOnlyList<RoleRow>>> ListRolesAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PermissionRow>>> ListPermissionsAsync(
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GrantRow>>> ListGrantsAsync(
        string roleId, CancellationToken cancellationToken);

    Task<Result<SaveOutcome>> SaveGrantsAsync(
        string roleId, IReadOnlyCollection<GrantRow> grants, CancellationToken cancellationToken);

    Task<Result<RoleRow>> CreateRoleAsync(
        string name, string description, string cloneFromRoleId,
        CancellationToken cancellationToken);

    Task<Result<RoleRow>> UpdateRoleAsync(
        string roleId, string name, string description, CancellationToken cancellationToken);

    Task<Result> DeleteRoleAsync(string roleId, CancellationToken cancellationToken);

    Task<Result> AssignRoleAsync(string email, string roleId, CancellationToken cancellationToken);

    Task<Result> UnassignRoleAsync(
        string email, string roleId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AuditRow>>> ListAuditAsync(
        int take, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserRow>>> ListUsersAsync(CancellationToken cancellationToken);

    Task<Result> SetUserActiveAsync(
        string accountId, bool isActive, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SessionRow>>> ListUserSessionsAsync(
        string accountId, CancellationToken cancellationToken);

    Task<Result> DeleteUserAsync(string accountId, CancellationToken cancellationToken);

    Task<Result<UserRow>> CreateUserAsync(
        string email, string displayName, string password, CancellationToken cancellationToken);

    Task<Result> UpdateUserAsync(
        string accountId, string displayName, string phone, string teamId,
        CancellationToken cancellationToken);

    Task<Result> ResetPasswordAsync(
        string accountId, string newPassword, CancellationToken cancellationToken);

    Task<Result> RevokeSessionAsync(
        string accountId, string sessionId, CancellationToken cancellationToken);
}

/// <summary>
/// The administrator's client for the authorization and account admin services.
/// </summary>
/// <remarks>
/// The server checks every call — <c>roles.manage</c> for the authorization half,
/// <c>users.manage</c> for the accounts; the client's own check only keeps buttons off the
/// screen. Failures arrive as <see cref="Result"/> rather than exceptions: they are expected
/// user-caused refusals, and the server's message is shown verbatim.
/// </remarks>
public sealed class AccessAdminService : IAccessAdminService
{
    private readonly AuthzV1.AuthzAdminService.AuthzAdminServiceClient _authz;
    private readonly AccountV1.AccountAdminService.AccountAdminServiceClient _accounts;

    public AccessAdminService(
        AuthzV1.AuthzAdminService.AuthzAdminServiceClient authz,
        AccountV1.AccountAdminService.AccountAdminServiceClient accounts)
    {
        ArgumentNullException.ThrowIfNull(authz);
        ArgumentNullException.ThrowIfNull(accounts);
        _authz = authz;
        _accounts = accounts;
    }

    public Task<Result<IReadOnlyList<RoleRow>>> ListRolesAsync(CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _authz
                .ListRolesAsync(new AuthzV1.ListRolesRequest(), cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<RoleRow> rows = [.. reply.Roles.Select(r => new RoleRow(
          r.Id, r.Name, r.Description, r.IsSystem, r.PermissionCount, r.AccountCount,
          reply.PermissionTotal))];
            return rows;
        });
    }

    public Task<Result<IReadOnlyList<PermissionRow>>> ListPermissionsAsync(CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _authz
                .ListPermissionsAsync(new AuthzV1.ListPermissionsRequest(), cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<PermissionRow> rows = [.. reply.Permissions.Select(p => new PermissionRow(
          p.Key, p.Name, p.Description, p.Category, p.IsHighRisk, p.IsScoped))];
            return rows;
        });
    }

    public Task<Result<IReadOnlyList<GrantRow>>> ListGrantsAsync(string roleId,
        CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _authz
                .GetRoleGrantsAsync(
                    new AuthzV1.GetRoleGrantsRequest { RoleId = roleId }, cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<GrantRow> rows =
                [.. reply.Grants.Select(g => new GrantRow(g.PermissionKey, ScopeName(g.Scope)))];
            return rows;
        });
    }

    public Task<Result<SaveOutcome>> SaveGrantsAsync(
        string roleId, IReadOnlyCollection<GrantRow> grants, CancellationToken ct)
    {
        return CallAsync(async () => {
            var request = new AuthzV1.SaveRoleGrantsRequest { RoleId = roleId };
            foreach (var grant in grants)
            {
                request.Grants.Add(new AuthzV1.Grant {
                    PermissionKey = grant.PermissionKey,
                    Scope = ScopeValue(grant.Scope),
                });
            }

            var reply = await _authz.SaveRoleGrantsAsync(request, cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);
            return new SaveOutcome(reply.Granted, reply.Revoked, reply.Rescoped);
        });
    }

    public Task<Result<RoleRow>> CreateRoleAsync(
        string name, string description, string cloneFromRoleId, CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _authz.CreateRoleAsync(
                new AuthzV1.CreateRoleRequest {
                    Name = name,
                    Description = description,
                    CloneFromRoleId = cloneFromRoleId,
                },
                cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

            var role = reply.Role;
            return new RoleRow(role.Id, role.Name, role.Description, role.IsSystem,
                role.PermissionCount, role.AccountCount);
        });
    }

    public Task<Result<RoleRow>> UpdateRoleAsync(
        string roleId, string name, string description, CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _authz.UpdateRoleAsync(
                new AuthzV1.UpdateRoleRequest {
                    RoleId = roleId,
                    Name = name,
                    Description = description,
                },
                cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

            var role = reply.Role;
            return new RoleRow(role.Id, role.Name, role.Description, role.IsSystem,
                role.PermissionCount, role.AccountCount);
        });
    }

    public Task<Result> DeleteRoleAsync(string roleId, CancellationToken ct)
    {
        return CallVoidAsync(() => _authz.DeleteRoleAsync(
              new AuthzV1.DeleteRoleRequest { RoleId = roleId }, cancellationToken: ct)
              .ResponseAsync);
    }

    public Task<Result> AssignRoleAsync(string email, string roleId, CancellationToken ct)
    {
        return CallVoidAsync(() => _authz.AssignRoleAsync(
              new AuthzV1.AssignRoleRequest { Email = email, RoleId = roleId },
              cancellationToken: ct).ResponseAsync);
    }

    public Task<Result> UnassignRoleAsync(string email, string roleId, CancellationToken ct)
    {
        return CallVoidAsync(() => _authz.UnassignRoleAsync(
              new AuthzV1.UnassignRoleRequest { Email = email, RoleId = roleId },
              cancellationToken: ct).ResponseAsync);
    }

    public Task<Result<IReadOnlyList<AuditRow>>> ListAuditAsync(int take, CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _authz.ListAuditEntriesAsync(
                new AuthzV1.ListAuditEntriesRequest { PageSize = take }, cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<AuditRow> rows = [.. reply.Entries.Select(e => new AuditRow(
          e.OccurredAt.ToDateTimeOffset().ToLocalTime(), e.ActorName, e.Action, e.RoleName,
          e.PermissionKey, e.Detail))];
            return rows;
        });
    }

    /// <summary>Lists the accounts and their roles, and joins the two.</summary>
    /// <remarks>
    /// Two calls because two server modules own the two halves: the account module owns people and
    /// the authorization module owns what they may do. Joining here costs one extra round trip;
    /// neither module holds a copy of the other's data, so the two cannot disagree.
    /// </remarks>
    public Task<Result<IReadOnlyList<UserRow>>> ListUsersAsync(CancellationToken ct)
    {
        return CallAsync(async () => {
            var accountsTask = _accounts
                .ListAccountsAsync(new AccountV1.ListAccountsRequest(), cancellationToken: ct)
                .ResponseAsync;
            var rolesTask = _authz
                .ListAccountRolesAsync(
                    new AuthzV1.ListAccountRolesRequest(), cancellationToken: ct)
                .ResponseAsync;

            var accounts = await accountsTask.ConfigureAwait(false);
            var roles = await rolesTask.ConfigureAwait(false);

            var byAccount = roles.Accounts.ToDictionary(
                entry => entry.AccountId,
                entry => (IReadOnlyList<string>)[.. entry.Roles.Select(r => r.Name)],
                StringComparer.Ordinal);

            IReadOnlyList<UserRow> rows = [.. accounts.Accounts.Select(a => new UserRow(
          a.Id,
          a.Email,
          a.DisplayName,
          a.Phone,
          a.TeamId,
          a.IsActive,
          a.LastSignInAt is null ? null : a.LastSignInAt.ToDateTimeOffset().ToLocalTime(),
          a.CreatedAt.ToDateTimeOffset().ToLocalTime(),
          a.ActiveSessionCount,
          byAccount.TryGetValue(a.Id, out var names) ? names : []))];
            return rows;
        });
    }

    public Task<Result> SetUserActiveAsync(string accountId, bool isActive, CancellationToken ct)
    {
        return CallVoidAsync(() => _accounts.SetAccountActiveAsync(
              new AccountV1.SetAccountActiveRequest { AccountId = accountId, IsActive = isActive },
              cancellationToken: ct).ResponseAsync);
    }

    public Task<Result<IReadOnlyList<SessionRow>>> ListUserSessionsAsync(
        string accountId, CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _accounts.ListAccountSessionsAsync(
                new AccountV1.ListAccountSessionsRequest { AccountId = accountId },
                cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

            IReadOnlyList<SessionRow> rows = [.. reply.Sessions.Select(s => new SessionRow(
          s.Id, s.Client, s.Device, s.IpAddress,
          s.CreatedAt.ToDateTimeOffset().ToLocalTime(),
          s.LastSeenAt.ToDateTimeOffset().ToLocalTime(),
          s.IsCurrent))];
            return rows;
        });
    }

    public Task<Result<UserRow>> CreateUserAsync(
        string email, string displayName, string password, CancellationToken ct)
    {
        return CallAsync(async () => {
            var reply = await _accounts.CreateAccountAsync(
                new AccountV1.CreateAccountRequest {
                    Email = email,
                    DisplayName = displayName,
                    Password = password,
                },
                cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

            var a = reply.Account;
            return new UserRow(a.Id, a.Email, a.DisplayName, a.Phone, a.TeamId, a.IsActive,
                a.LastSignInAt is null ? null : a.LastSignInAt.ToDateTimeOffset().ToLocalTime(),
                a.CreatedAt.ToDateTimeOffset().ToLocalTime(), a.ActiveSessionCount, []);
        });
    }

    public Task<Result> UpdateUserAsync(
        string accountId, string displayName, string phone, string teamId, CancellationToken ct)
    {
        return CallVoidAsync(() => _accounts.UpdateAccountAsync(
            new AccountV1.UpdateAccountRequest {
                AccountId = accountId,
                DisplayName = displayName,
                Phone = phone,
                TeamId = teamId,
            },
            cancellationToken: ct).ResponseAsync);
    }

    public Task<Result> ResetPasswordAsync(
        string accountId, string newPassword, CancellationToken ct)
    {
        return CallVoidAsync(() => _accounts.ResetPasswordAsync(
            new AccountV1.ResetPasswordRequest {
                AccountId = accountId,
                NewPassword = newPassword,
            },
            cancellationToken: ct).ResponseAsync);
    }

    public Task<Result> RevokeSessionAsync(
        string accountId, string sessionId, CancellationToken ct)
    {
        return CallVoidAsync(() => _accounts.RevokeAccountSessionAsync(
            new AccountV1.RevokeAccountSessionRequest {
                AccountId = accountId,
                SessionId = sessionId,
            },
            cancellationToken: ct).ResponseAsync);
    }

    public Task<Result> DeleteUserAsync(string accountId, CancellationToken ct)
    {
        return CallVoidAsync(() => _accounts.DeleteAccountAsync(
            new AccountV1.DeleteAccountRequest { AccountId = accountId },
            cancellationToken: ct).ResponseAsync);
    }

    /// <summary>Runs a call and turns an RPC failure into the message the server wrote.</summary>
    private static async Task<Result<T>> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return Result.Success(await call().ConfigureAwait(false));
        }
        catch (RpcException exception)
        {
            return Result.Failure<T>(ToError(exception));
        }
    }

    private static async Task<Result> CallVoidAsync<T>(Func<Task<T>> call)
    {
        try
        {
            await call().ConfigureAwait(false);
            return Result.Success();
        }
        catch (RpcException exception)
        {
            return Result.Failure(ToError(exception));
        }
    }

    /// <summary>
    /// Turns a status into an error carrying the server's own sentence.
    /// </summary>
    /// <remarks>
    /// The server writes these to be read by a person — "Admin ships with the product and cannot be
    /// deleted" — and they are shown verbatim. The code is kept as the error identifier so a caller
    /// that wants to branch still can.
    /// </remarks>
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
                    "You no longer have permission for this. Ask an administrator to restore it.",
                StatusCode.Unauthenticated => "Your session has ended. Sign in again.",
                StatusCode.Unavailable => "The server could not be reached.",
                _ => "The server could not complete that request.",
            };
        }

        return new Error(exception.StatusCode.ToString(), detail);
    }

    private static string ScopeName(AuthzV1.Scope scope)
    {
        return scope switch {
            AuthzV1.Scope.Own => "own",
            AuthzV1.Scope.Team => "team",
            AuthzV1.Scope.All => "all",
            _ => string.Empty,
        };
    }

    private static AuthzV1.Scope ScopeValue(string scope)
    {
        return scope switch {
            "own" => AuthzV1.Scope.Own,
            "team" => AuthzV1.Scope.Team,
            "all" => AuthzV1.Scope.All,
            _ => AuthzV1.Scope.Unspecified,
        };
    }
}
