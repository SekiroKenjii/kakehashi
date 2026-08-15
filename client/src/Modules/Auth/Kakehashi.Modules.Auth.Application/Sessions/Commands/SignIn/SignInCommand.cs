using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn;

/// <summary>
/// Signs in. Carries credentials when the app collects them itself; leaves them null when the
/// registered authenticator hands the user to the authorization server's own page.
/// </summary>
public sealed record SignInCommand(SignInCredentials? Credentials = null) : IRequest<Result>;
