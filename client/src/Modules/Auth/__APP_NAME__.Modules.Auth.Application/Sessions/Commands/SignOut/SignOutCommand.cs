using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.SignOut;

/// <summary>Signs the user out: clears the local session and ends the server session.</summary>
public sealed record SignOutCommand : IRequest<Result>;
