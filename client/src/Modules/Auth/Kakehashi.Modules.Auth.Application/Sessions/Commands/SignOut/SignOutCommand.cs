using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut;

/// <summary>Signs the user out: clears the local session and ends the server session.</summary>
public sealed record SignOutCommand : IRequest<Result>;
