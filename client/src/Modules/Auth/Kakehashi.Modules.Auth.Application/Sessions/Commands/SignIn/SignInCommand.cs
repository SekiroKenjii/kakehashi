using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.SignIn {
  public sealed record SignInCommand(SignInCredentials? Credentials = null) : IRequest<Result>;
}
