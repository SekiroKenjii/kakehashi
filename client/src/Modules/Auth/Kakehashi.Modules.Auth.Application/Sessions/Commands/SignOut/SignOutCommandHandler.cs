using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Sessions.Events;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.SignOut {
  public sealed class SignOutCommandHandler : IRequestHandler<SignOutCommand, Result> {
    private readonly IInteractiveAuthenticator _authenticator;
    private readonly IAuthSessionAccessor _session;
    private readonly ITokenStore _tokenStore;
    private readonly IPublisher _publisher;

    public SignOutCommandHandler(
        IInteractiveAuthenticator authenticator,
        IAuthSessionAccessor session,
        ITokenStore tokenStore,
        IPublisher publisher) {
      _authenticator = authenticator;
      _session = session;
      _tokenStore = tokenStore;
      _publisher = publisher;
    }

    public async Task<Result> Handle(SignOutCommand request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);

      await _authenticator.LogoutAsync(_session.Current, cancellationToken);
      _session.Clear();
      await _tokenStore.ClearAsync(cancellationToken);
      await _publisher.Publish(new UserSignedOutNotification(), cancellationToken);

      return Result.Success();
    }
  }
}
