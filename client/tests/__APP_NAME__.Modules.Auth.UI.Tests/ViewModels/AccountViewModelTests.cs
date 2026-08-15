using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Account;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Commands.ChangeRemotePassword;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Queries.GetRemoteProfile;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.SignIn;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.SignOut;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity;
using __ROOT_NAMESPACE__.Modules.Auth.UI.ViewModels;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using NSubstitute;
using Xunit;

namespace __ROOT_NAMESPACE__.Modules.Auth.UI.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="AccountViewModel"/>: the signed-in / signed-out states, the mapping
/// and client-side paging of sessions and security activity, the session-action flows, and the
/// edit-profile / change-password dialog logic. Every gateway call goes through a substituted
/// <see cref="ISender"/> returning <see cref="Result"/>s.
/// </summary>
public sealed class AccountViewModelTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private SessionDto _session = SignedOut();

    public AccountViewModelTests()
    {
        // Sensible success defaults so any path under test has non-null awaitables; tests override.
        _sender.Send(Arg.Is<GetCurrentSessionQuery>(request => request != null)).Returns(_ => Task.FromResult(_session));
        _sender.Send(Arg.Is<GetRemoteSessionsQuery>(request => request != null))
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<RemoteSessionDto>>([])));
        _sender.Send(Arg.Is<GetSecurityActivityQuery>(request => request != null))
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<SecurityEventDto>>([])));
        _sender.Send(Arg.Is<SignInCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
        _sender.Send(Arg.Is<SignOutCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
        _sender.Send(Arg.Is<RevokeAllSessionsCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
        _sender.Send(Arg.Is<RevokeRemoteSessionCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
        _sender.Send(Arg.Is<GetRemoteProfileQuery>(request => request != null)).Returns(Task.FromResult(
            Result.Success(new RemoteProfileDto("Vo", "vo@example.com", "123", false))));
        _sender.Send(Arg.Is<UpdateRemoteProfileCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
        _sender.Send(Arg.Is<ChangeRemotePasswordCommand>(request => request != null)).Returns(Task.FromResult(Result.Success()));
    }

    [Fact]
    public async Task Load_WhenSignedOut_ShowsSignedOutState()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsSignedOut);
        Assert.True(viewModel.CanSignIn);
        Assert.False(viewModel.CanSignOut);
        Assert.Empty(viewModel.Sessions);
        Assert.Equal("ACTIVE SESSIONS", viewModel.SessionsHeader);
    }

    [Fact]
    public async Task Load_WhenSignedIn_PopulatesIdentityAndTitleCasedRole()
    {
        _session = SignedIn("Vo Thuong", ["admin"]);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal("Vo Thuong", viewModel.DisplayName);
        Assert.Equal("vo@example.com", viewModel.Email);
        Assert.Equal("Admin", viewModel.RoleText);
        Assert.True(viewModel.HasRole);
        Assert.True(viewModel.CanSignOut);
        Assert.False(viewModel.CanSignIn);
    }

    [Fact]
    public async Task Load_WhenSignedIn_MapsSessions()
    {
        _session = SignedIn();
        _sender.Send(Arg.Is<GetRemoteSessionsQuery>(request => request != null)).Returns(Task.FromResult(
            Result.Success<IReadOnlyList<RemoteSessionDto>>([
                new RemoteSessionDto("s1", "Edge", "Windows", "1.2.3.4", Now(), Now(), IsCurrent: true),
            ])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        var item = Assert.Single(viewModel.Sessions);
        Assert.Equal("Windows · Edge", item.Title);
        Assert.True(item.IsCurrent);
        Assert.False(item.IsNotCurrent);
        Assert.Equal("ACTIVE SESSIONS (1)", viewModel.SessionsHeader);
    }

    [Fact]
    public async Task Load_WhenSignedIn_PagesSessionsFivePerPage()
    {
        _session = SignedIn();
        var sessions = Enumerable
            .Range(0, 7)
            .Select(i => new RemoteSessionDto(
                $"s{i}", "Edge", "Windows", null, Now(), Now(), IsCurrent: false))
            .ToList();
        _sender.Send(Arg.Is<GetRemoteSessionsQuery>(request => request != null)).Returns(Task.FromResult(
            Result.Success<IReadOnlyList<RemoteSessionDto>>(sessions)));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);
        Assert.Equal(5, viewModel.Sessions.Count);
        Assert.True(viewModel.HasSessionsPaging);
        Assert.Equal("1 / 2", viewModel.SessionsPageLabel);

        viewModel.SessionsNextPageCommand.Execute(parameter: null);
        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("2 / 2", viewModel.SessionsPageLabel);
    }

    [Fact]
    public async Task Load_WhenSignedIn_MapsSecurityActivityWithAlertFlag()
    {
        _session = SignedIn();
        _sender.Send(Arg.Is<GetSecurityActivityQuery>(request => request != null)).Returns(Task.FromResult(
            Result.Success<IReadOnlyList<SecurityEventDto>>([
                new SecurityEventDto("FailedSignIn", "Windows", "1.2.3.4", Now()),
                new SecurityEventDto("SignedIn", "Windows", "1.2.3.4", Now()),
            ])));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal("Failed sign-in attempt", viewModel.Activity[0].Title);
        Assert.True(viewModel.Activity[0].IsAlert);
        Assert.Equal("Signed in", viewModel.Activity[1].Title);
        Assert.False(viewModel.Activity[1].IsAlert);
    }

    [Fact]
    public async Task SignOutEverywhere_WhenRevokeFails_SetsErrorAndDoesNotSignOut()
    {
        _sender.Send(Arg.Is<RevokeAllSessionsCommand>(request => request != null))
            .Returns(Task.FromResult(Result.Failure(new Error("Revoke.Failed", "boom"))));
        var viewModel = CreateViewModel();

        await viewModel.SignOutEverywhereCommand.ExecuteAsync(parameter: null);

        Assert.Equal("boom", viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        await _sender.DidNotReceive().Send(Arg.Any<SignOutCommand>());
    }

    [Fact]
    public async Task SignOutEverywhere_WhenRevokeSucceeds_SignsOut()
    {
        var viewModel = CreateViewModel();

        await viewModel.SignOutEverywhereCommand.ExecuteAsync(parameter: null);

        await _sender.Received(1).Send(Arg.Any<SignOutCommand>());
    }

    [Fact]
    public async Task RevokeSession_WhenFails_SetsError()
    {
        _sender.Send(Arg.Is<RevokeRemoteSessionCommand>(request => request != null))
            .Returns(Task.FromResult(Result.Failure(new Error("Revoke.Failed", "nope"))));
        var viewModel = CreateViewModel();

        await viewModel.RevokeSessionCommand.ExecuteAsync(
            new SessionItem("s1", "title", "subtitle", IsCurrent: false));

        Assert.Equal("nope", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ChangePassword_WhenFieldsEmpty_FailsWithoutSending()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentPassword = "";
        viewModel.NewPassword = "";

        bool ok = await viewModel.ChangePasswordAsync();

        Assert.False(ok);
        Assert.Equal("Enter your current and new password.", viewModel.DialogError);
        await _sender.DidNotReceive().Send(Arg.Any<ChangeRemotePasswordCommand>());
    }

    [Fact]
    public async Task ChangePassword_WhenConfirmationMismatches_Fails()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentPassword = "old";
        viewModel.NewPassword = "alpha";
        viewModel.ConfirmPassword = "beta";

        bool ok = await viewModel.ChangePasswordAsync();

        Assert.False(ok);
        Assert.Equal("The new password and its confirmation do not match.", viewModel.DialogError);
        await _sender.DidNotReceive().Send(Arg.Any<ChangeRemotePasswordCommand>());
    }

    [Fact]
    public async Task ChangePassword_WhenValid_SendsCommand()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentPassword = "old";
        viewModel.NewPassword = "new-secret";
        viewModel.ConfirmPassword = "new-secret";

        bool ok = await viewModel.ChangePasswordAsync();

        Assert.True(ok);
        await _sender.Received(1).Send(Arg.Any<ChangeRemotePasswordCommand>());
    }

    [Fact]
    public async Task ChangePassword_WhenServerRejects_SetsDialogError()
    {
        _sender.Send(Arg.Is<ChangeRemotePasswordCommand>(request => request != null))
            .Returns(Task.FromResult(Result.Failure(new Error("Password.Weak", "too weak"))));
        var viewModel = CreateViewModel();
        viewModel.CurrentPassword = "old";
        viewModel.NewPassword = "new-secret";
        viewModel.ConfirmPassword = "new-secret";

        bool ok = await viewModel.ChangePasswordAsync();

        Assert.False(ok);
        Assert.Equal("too weak", viewModel.DialogError);
    }

    [Fact]
    public async Task SaveProfile_WhenSucceeds_UpdatesDisplayName()
    {
        var viewModel = CreateViewModel();
        viewModel.EditDisplayName = "New Name";

        bool ok = await viewModel.SaveProfileAsync();

        Assert.True(ok);
        Assert.Equal("New Name", viewModel.DisplayName);
    }

    [Fact]
    public async Task SaveProfile_WhenFails_SetsDialogError()
    {
        _sender.Send(Arg.Is<UpdateRemoteProfileCommand>(request => request != null))
            .Returns(Task.FromResult(Result.Failure(new Error("Profile.Invalid", "bad"))));
        var viewModel = CreateViewModel();
        viewModel.EditDisplayName = "New Name";

        bool ok = await viewModel.SaveProfileAsync();

        Assert.False(ok);
        Assert.Equal("bad", viewModel.DialogError);
    }

    [Fact]
    public void PrepareChangePassword_ResetsFieldsAndError()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentPassword = "x";
        viewModel.NewPassword = "y";
        viewModel.ConfirmPassword = "z";

        viewModel.PrepareChangePassword();

        Assert.Equal("", viewModel.CurrentPassword);
        Assert.Equal("", viewModel.NewPassword);
        Assert.Equal("", viewModel.ConfirmPassword);
        Assert.False(viewModel.HasDialogError);
    }

    [Fact]
    public async Task PrepareEditProfile_PrefillsFromServer()
    {
        _sender.Send(Arg.Is<GetRemoteProfileQuery>(request => request != null)).Returns(Task.FromResult(
            Result.Success(new RemoteProfileDto("Server Name", "e@x.com", "555", false))));
        var viewModel = CreateViewModel();

        await viewModel.PrepareEditProfileAsync();

        Assert.Equal("Server Name", viewModel.EditDisplayName);
        Assert.Equal("555", viewModel.EditPhone);
    }

    private AccountViewModel CreateViewModel()
    {
        return new AccountViewModel(_sender, Dialogs());
    }

    private static DateTimeOffset Now()
    {
        return DateTimeOffset.UtcNow;
    }

    private static SessionDto SignedOut()
    {
        return new SessionDto(false, null, null, null, null, []);
    }

    private static SessionDto SignedIn(string? displayName = "Vo", IReadOnlyList<string>? roles = null)
    {
        return new SessionDto(
            true,
            displayName,
            "vo@example.com",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(-2),
            roles ?? []);
    }

    /// <summary>
    /// A dialog service that says yes.
    /// </summary>
    /// <remarks>
    /// Sign-out-everywhere confirms before revoking, and a substitute returning the default would
    /// answer "cancel" — so every test of that path would pass by never running it.
    /// </remarks>
    private static IDialogService Dialogs()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowConfirmAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        return dialogs;
    }
}
