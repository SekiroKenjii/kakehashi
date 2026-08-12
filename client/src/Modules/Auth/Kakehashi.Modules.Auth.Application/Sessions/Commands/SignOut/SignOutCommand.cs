using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut {
  public sealed record SignOutCommand : IRequest<Result>;
}
