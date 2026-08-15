using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.SignIn;

/// <summary>
/// Signs in. Carries credentials when the app collects them itself; leaves them null when the
/// registered authenticator hands the user to the authorization server's own page.
/// </summary>
public sealed record SignInCommand(SignInCredentials? Credentials = null) : IRequest<Result>;
