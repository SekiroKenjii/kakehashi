using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Queries.GetRemoteProfile {
  public sealed class GetRemoteProfileQueryHandler
      : IRequestHandler<GetRemoteProfileQuery, Result<RemoteProfileDto>> {
    private readonly IAccountGateway _account;

    public GetRemoteProfileQueryHandler(IAccountGateway account) {
      ArgumentNullException.ThrowIfNull(account);
      _account = account;
    }

    public Task<Result<RemoteProfileDto>> Handle(
        GetRemoteProfileQuery request, CancellationToken cancellationToken) {
      return _account.GetProfileAsync(cancellationToken);
    }
  }
}
