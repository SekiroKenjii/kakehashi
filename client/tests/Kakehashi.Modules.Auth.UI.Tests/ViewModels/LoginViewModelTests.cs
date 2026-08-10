using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.Modules.Auth.UI.Infrastructure;
using Kakehashi.Modules.Auth.UI.ViewModels;
using Kakehashi.SharedKernel;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Auth.UI.Tests.ViewModels {
  /// <summary>
  /// Unit tests for <see cref="LoginViewModel"/>: which screen each <see cref="AuthMode"/> shows,
  /// what the sign-in command sends in each, and the guards around a half-filled form.
  /// </summary>
  public sealed class LoginViewModelTests {
    private readonly ISender _sender = Substitute.For<ISender>();

    private static AuthOptions Options(AuthMode mode) {
      return new AuthOptions {
        Authority = "http://localhost:8080",
        ClientId = "kakehashi-desktop",
        Mode = mode,
      };
    }

    private LoginViewModel CreateViewModel(AuthMode mode) {
      var options = Options(mode);
      return new LoginViewModel(
          _sender, new SystemBrowser(options.RedirectUri), Microsoft.Extensions.Options.Options
              .Create(options));
    }

    private void SendReturns(Result result) {
      _sender.Send(Arg.Is<SignInCommand>(command => command != null), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult(result));
    }

    [Fact]
    public void InAppMode_ShowsTheCredentialFormAndNeverTheBrowserScreens() {
      var viewModel = CreateViewModel(AuthMode.InApp);

      Assert.True(viewModel.ShowsCredentialForm);
      Assert.False(viewModel.ShowsBrowserPrompt);
      Assert.False(viewModel.ShowsBrowserWaiting);
      Assert.False(viewModel.ShowsBrowserError);
    }

    [Fact]
    public void BrowserMode_ShowsTheBrowserPromptAndNeverTheForm() {
      var viewModel = CreateViewModel(AuthMode.Browser);

      Assert.False(viewModel.ShowsCredentialForm);
      Assert.True(viewModel.ShowsBrowserPrompt);
    }

    [Fact]
    public void InAppMode_CannotSignInUntilBothFieldsAreFilled() {
      var viewModel = CreateViewModel(AuthMode.InApp);
      Assert.False(viewModel.SignInCommand.CanExecute(null));

      viewModel.Email = "dev@kakehashi.local";
      Assert.False(viewModel.SignInCommand.CanExecute(null));

      viewModel.Password = "passphrase";
      Assert.True(viewModel.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void BrowserMode_CanSignInImmediately() {
      // Nothing is collected here, so there is nothing to wait for.
      Assert.True(CreateViewModel(AuthMode.Browser).SignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task InAppMode_SendsTheTypedCredentialsWithTheEmailTrimmed() {
      SendReturns(Result.Success());
      var viewModel = CreateViewModel(AuthMode.InApp);
      viewModel.Email = "  dev@kakehashi.local  ";
      viewModel.Password = "kakehashi dev passphrase";

      await viewModel.SignInCommand.ExecuteAsync(null);

      await _sender.Received(1).Send(
          Arg.Is<SignInCommand>(command =>
              command != null
              && command.Credentials != null
              && command.Credentials.Email == "dev@kakehashi.local"
              && command.Credentials.Password == "kakehashi dev passphrase"),
          Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowserMode_SendsNoCredentials() {
      SendReturns(Result.Success());

      await CreateViewModel(AuthMode.Browser).SignInCommand.ExecuteAsync(null);

      await _sender.Received(1).Send(
          Arg.Is<SignInCommand>(command => command != null && command.Credentials == null),
          Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulSignIn_RaisesSignInSucceededAndDropsThePassword() {
      SendReturns(Result.Success());
      var viewModel = CreateViewModel(AuthMode.InApp);
      viewModel.Email = "dev@kakehashi.local";
      viewModel.Password = "kakehashi dev passphrase";
      bool raised = false;
      viewModel.SignInSucceeded += (_, _) => raised = true;

      await viewModel.SignInCommand.ExecuteAsync(null);

      Assert.True(raised);
      Assert.Equal(string.Empty, viewModel.Password);
    }

    [Fact]
    public async Task FailedSignIn_ShowsTheErrorAndKeepsTheEmail() {
      SendReturns(Result.Failure(AuthErrors.LoginFailed));
      var viewModel = CreateViewModel(AuthMode.InApp);
      viewModel.Email = "dev@kakehashi.local";
      viewModel.Password = "wrong";

      await viewModel.SignInCommand.ExecuteAsync(null);

      Assert.True(viewModel.HasError);
      Assert.Equal(AuthErrors.LoginFailed.Message, viewModel.ErrorMessage);
      // The form stays put so the user can correct one field rather than retype both.
      Assert.True(viewModel.ShowsCredentialForm);
      Assert.Equal("dev@kakehashi.local", viewModel.Email);
    }

    [Fact]
    public async Task CancelledSignIn_ShowsNoError() {
      // The user closing the browser is not a failure worth a red banner.
      SendReturns(Result.Failure(AuthErrors.LoginCancelled));
      var viewModel = CreateViewModel(AuthMode.Browser);

      await viewModel.SignInCommand.ExecuteAsync(null);

      Assert.False(viewModel.HasError);
      Assert.True(viewModel.ShowsBrowserPrompt);
    }

    [Fact]
    public async Task FailedSignIn_InBrowserMode_SwitchesToTheErrorScreen() {
      SendReturns(Result.Failure(AuthErrors.LoginFailed));
      var viewModel = CreateViewModel(AuthMode.Browser);

      await viewModel.SignInCommand.ExecuteAsync(null);

      Assert.True(viewModel.ShowsBrowserError);
      Assert.False(viewModel.ShowsBrowserPrompt);
      Assert.False(viewModel.ShowsBrowserWaiting);
    }
  }
}
